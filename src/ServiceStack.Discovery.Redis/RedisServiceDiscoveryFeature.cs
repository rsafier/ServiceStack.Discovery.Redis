using System;
using System.Collections.Generic;
using System.Linq;
using ServiceStack.Redis;
using ServiceStack;
using ServiceStack.Text;
using System.Threading;
using ServiceStack.Logging;
using ServiceStack.DataAnnotations;
using ServiceStack.Web;
using ServiceStack.Redis.Pipeline;
using System.Net;

namespace ServiceStack.Discovery.Redis
{
    public enum RedisDiscoveryRoles
    {
        Node, //Default always have
        CanHostMaster, //If not HostMaster and can fufill role if required
        HostMaster //Is current HostMaster for Host (if multiple nodes/services are on a single host 1 will be selected to maintain host level key(s))
    }

    public class RedisDiscoveryNodeInfo : IMeta
    {
        public Guid NodeId { get; set; }
        public string HostName { get; set; }
        public string ServiceName { get; set; }
        public string WebHostUrl { get; set; }
        public TimeSpan Uptime { get; set; }
        public List<RedisDiscoveryRoles> Roles { get; set; } = new List<RedisDiscoveryRoles>() { RedisDiscoveryRoles.Node };
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        public DateTime LastUpdateOn { get; set; }
    }

    public class RedisHostMasterInfo : IMeta
    {
        /// <summary>
        /// InstanceId of node which is acting HostMaster
        /// </summary>
        public Guid NodeId { get; set; }       
        public string HostName { get; set; }
        public string WebHostUrl { get; set; }
        public TimeSpan Uptime { get; set; }
        public Dictionary<string, string> Meta { get; set; } = new Dictionary<string, string>();
        public DateTime LastUpdateOn { get; set; }

    }

    /// <summary>
    /// Will prevent service from being published for discovery.
    /// </summary>
    public class ExcludeServiceDiscoveryAttribute : Attribute
    {
    }

    [Route("/RedisServiceDiscovery/GetServiceRequestTypes")]
    [Exclude(Feature.Metadata)]
    [Restrict(VisibilityTo = RequestAttributes.None)]
    public class GetServiceRequestTypes : IReturn<List<string>>
    {
    }
    [Route("/RedisServiceDiscovery/GetActiveHosts")]
    [Exclude(Feature.Metadata)]
    [Restrict(VisibilityTo = RequestAttributes.None)]
    public class GetActiveHosts : IReturn<List<RedisHostMasterInfo>>
    {
    }
    [Route("/RedisServiceDiscovery/GetActiveNodes")]
    [Exclude(Feature.Metadata)]
    [Restrict(VisibilityTo = RequestAttributes.None)]
    public class GetActiveNodes : IReturn<List<RedisDiscoveryNodeInfo>>
    {
    }
    [Route("/RedisServiceDiscovery/ResolveBaseUrl/{TypeFullName}")]
    [Exclude(Feature.Metadata)]
    [Restrict(VisibilityTo = RequestAttributes.None)]
    public class ResolveBaseUrl : IReturn<string>
    {
        public string TypeFullName { get; set; }
    }
    /// <summary>
    /// Will return dictonary of nodeIds, and BaseURLs (nodeId can be used to get node details for better choices)
    /// </summary>
    [Route("/RedisServiceDiscovery/ResolveNodesForRequest/{TypeFullName}")]
    [Exclude(Feature.Metadata)]
    [Restrict(VisibilityTo = RequestAttributes.None)]
    public class ResolveNodesForRequest : IReturn<Dictionary<string, string>>
    {
        public string TypeFullName { get; set; }
    }

    /// <summary>
    /// Provides Redis backed service-discovery for ServiceStack 4.0.56 or greater    
    /// </summary>
    public class RedisServiceDiscoveryFeature : IPlugin, IDisposable
    {
        private readonly ILog Log = LogManager.GetLogger(typeof(RedisServiceDiscoveryFeature));
        private Timer BackgroundLoopTimer;
        private List<string> typeNames;
        private List<string> TypeNames
        {
            get
            {
                if (typeNames != null) return typeNames;

                var nativeTypes = HostContext.AppHost.GetPlugin<NativeTypesFeature>();
                typeNames = HostContext.AppHost.Metadata.RequestTypes
                    .Where(x => x.AllAttributes<ExcludeAttribute>().All(a => a.Feature != Feature.Metadata))
                    .Where(x => !nativeTypes.MetadataTypesConfig.IgnoreTypes.Contains(x))
                    .Where(x => !nativeTypes.MetadataTypesConfig.IgnoreTypesInNamespaces.Contains(x.Namespace))
                    .Where(x => x.AllAttributes<RestrictAttribute>().All(a => a.VisibilityTo.HasFlag(RequestAttributes.External)))
                    .Where(x => !x.HasAttribute< ExcludeServiceDiscoveryAttribute>())
                    .Select(z => z.FullName).ToList();
                return typeNames;
            }
        }

        public string RedisNodeKey { get; private set; }

        /// <summary>
        /// Only valid if IsHostMaster
        /// </summary>
        public RedisHostMasterInfo HostMasterInfo { get; private set; } = new RedisHostMasterInfo();

        /// <summary>
        /// Global Id for service instance, can be used to diff. between multiple off same service on same host
        /// </summary>
        public Guid NodeId { get; private set; } = Guid.NewGuid();
        /// <summary>
        /// Redis key used by HostMaster role
        /// </summary>
        public string RedisHostKey { get; private set; }
        /// <summary>
        /// Redis key prefix
        /// </summary>
        public string RedisPrefix { get; private set; } = "rsd";

        /// <summary>
        /// Node level config
        /// </summary>
        public RedisDiscoveryNodeInfo Config { get; private set; } = new RedisDiscoveryNodeInfo();
        /// <summary>
        /// How often node refreshes its data to RSD Redis storage instance
        /// </summary>
        public TimeSpan NodeRefreshPeriod { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>
        /// How quickly RSD data from this node will expire if not refreshed (RSD will assume your dead, because your keys will be expired via TTL on Redis server). 
        /// </summary>
        public TimeSpan NodeTimeoutPeriod { get; set; } = TimeSpan.FromSeconds(30);

        public bool CanHostMaster
        {
            get
            { return Config.Roles.Contains(RedisDiscoveryRoles.CanHostMaster); }
            set
            {
                if (value)
                    Config.Roles.AddIfNotExists(RedisDiscoveryRoles.CanHostMaster);
                else
                    Config.Roles.Remove(RedisDiscoveryRoles.CanHostMaster);
            }
        }

        public string HostName { get; set; } = Dns.GetHostName();

        /// <summary>
        /// Is this node also the HostMaster?
        /// </summary>
        public bool IsHostMaster
        {
            get
            {
                return Config.Roles.Contains(RedisDiscoveryRoles.HostMaster);
            }
            set
            {
                if (value)
                    Config.Roles.AddIfNotExists(RedisDiscoveryRoles.HostMaster);
                else
                    Config.Roles.Remove(RedisDiscoveryRoles.HostMaster);
            }
        }
        /// <summary>
        /// Registration point for custom actions on node refresh
        /// </summary>
        public List<Action> OnNodeRefreshActions { get; private set; } = new List<Action>();
        public List<Action> OnHostRefreshActions { get; private set; } = new List<Action>();
        public RedisServiceDiscoveryFeature()
        {
            RedisHostKey = "rsd:host:{0}".Fmt(HostName);
            CanHostMaster = true;

        }
        public RedisServiceDiscoveryFeature(string redisPrefix = "rsd", bool canHostMaster = true)
        {
            RedisPrefix = redisPrefix;
            CanHostMaster = canHostMaster;
            RedisHostKey = "{0}:host:{1}".Fmt(RedisPrefix, HostName);
        }

        public void Register(IAppHost appHost)
        {
            if (appHost.TryResolve<IRedisClientsManager>() == null)
                throw new Exception("Required IRedisClientsManager to be registered in IOC.");
            appHost.RegisterService<RedisServiceDiscoveryServices>();
            appHost.AfterInitCallbacks.Add(StartBackgroundLoopTimer);
            appHost.OnDisposeCallbacks.Add(DisposeCallback);

            RedisNodeKey = "{0}:node:{1}:{2}:{3}".Fmt(RedisPrefix, HostName, HostContext.ServiceName, NodeId);
            Config.NodeId = NodeId;
            Config.HostName = HostName;
            Config.ServiceName = HostContext.ServiceName;
            Config.WebHostUrl = appHost.Config.WebHostUrl;
            HostMasterInfo.WebHostUrl = Config.WebHostUrl;
            HostMasterInfo.NodeId = Config.NodeId;

            appHost.GetContainer().Register<IServiceGatewayFactory>(c => new RedisServiceDiscoveryGateway()).ReusedWithin(Funq.ReuseScope.None);
        }

        private void StartBackgroundLoopTimer(IAppHost obj)
        { 
            BackgroundLoopTimer = new Timer(TimerTick, null, 0, 0); //start timer and whole SCC process
        }

        private void DisposeCallback(IAppHost obj)
        {
            Dispose();
        }


        public string GetHostURI(object dto)
        {
            if (dto == null)
                throw new ArgumentNullException(nameof(dto));
            return HostContext.AppHost.ExecuteService(new ResolveBaseUrl { TypeFullName = dto.GetType().FullName }) as string;
        }

        public string ResolveBaseUrl(Type dtoType)
        {
            if (dtoType == null)
                throw new ArgumentNullException(nameof(dtoType));
            return HostContext.AppHost.ExecuteService(new ResolveBaseUrl { TypeFullName = dtoType.FullName }) as string;
        }

        private void TimerTick(object state)
        {
            BackgroundLoopTimer.Change(Timeout.Infinite, 0); //stop timer until finished with loop
            HostMasterInfo.LastUpdateOn = Config.LastUpdateOn = DateTime.UtcNow;
            HostMasterInfo.Uptime = Config.Uptime = Config.LastUpdateOn - HostContext.AppHost.ReadyAt.Value;
            RegisterToRSD();
            BackgroundLoopTimer.Change((int)NodeRefreshPeriod.TotalMilliseconds, (int)NodeRefreshPeriod.TotalMilliseconds); //done with loop turn timer back on.
        }

        private void RegisterToRSD()
        {
            using (var r = HostContext.AppHost.GetRedisClient())
            {
                using (var p = r.CreatePipeline())
                {
                    RegisterTypes(p);
                    RegisterNode(p);
                    p.Flush();
                }
                if (CanHostMaster)
                {
                    HandleHostMasterRole(r);
                }
                OnNodeRefreshActions.Each(a => a());
            }
        }

        private void RegisterNode(IRedisPipeline p)
        {
            p.QueueCommand(q => q.Set(RedisNodeKey, Config, NodeTimeoutPeriod));            
        }
         

        private void RegisterTypes(IRedisPipeline p)
        {
            foreach (var typeName in TypeNames)
            {
                p.QueueCommand(q => q.Set("{0}:req:{1}:{2}".Fmt(RedisPrefix, typeName, NodeId.ToString()), HostContext.AppHost.Config.WebHostUrl, NodeTimeoutPeriod));
            }
        }

        private void HandleHostMasterRole(IRedisClient r)
        {
            var currentMaster = r.Get<RedisHostMasterInfo>(RedisHostKey);
            var hasTakenMasterRole = false;
            if (currentMaster == null)//try to become master
            { 
                if (r.SetValueIfNotExists(RedisHostKey, HostMasterInfo.ToJson()))
                {
                    IsHostMaster = true;
                    hasTakenMasterRole = true;
                    r.ExpireEntryIn(RedisHostKey, NodeTimeoutPeriod); //give an extra 1 second to refresh.
                }
                else
                {
                    //we lost, oh well.
                    IsHostMaster = false;
                }
            }
            if (IsHostMaster) //We think we are master
            {                
                if (currentMaster == null) currentMaster = r.Get<RedisHostMasterInfo>(RedisHostKey);
                if (currentMaster.NodeId.Equals(NodeId))
                {   //Yep its ours, update & kick timeout 
                    OnHostRefreshActions.ForEach(a => a()); //run any registered actions for Host
                    r.Set(RedisHostKey, HostMasterInfo, NodeTimeoutPeriod);
                    r.AddItemToSortedSet("{0}:hosts:lastseen".Fmt(RedisPrefix), HostName, DateTime.UtcNow.ToUnixTime()); //score is unixtime UTC useful to figure out when a host dropped out
                    if (hasTakenMasterRole)
                    {
                        Log.DebugFormat("NodeId:{0} has taken HostMaster role", NodeId, currentMaster.NodeId);
                    }
                }
                else
                {
                    IsHostMaster = false;
                    Log.DebugFormat("NodeId:{0} has lost HostMaster role to {1}", NodeId, currentMaster.NodeId);
                }
            }
        }        

        public void Dispose()
        {
            BackgroundLoopTimer.Dispose();
        }
    }

    public class RedisServiceDiscoveryServices : Service
    {
        public Dictionary<string, string> Any(ResolveNodesForRequest req)
        {
            if (req.TypeFullName.IsEmpty())
                return null;
            var typeKey = "{0}:req:{1}:".Fmt(HostContext.GetPlugin<RedisServiceDiscoveryFeature>().RedisPrefix, req.TypeFullName);
            var keys = Redis.GetKeysByPattern(typeKey + "*");
            if (keys.Any())
            {
                return Redis.GetAll<string>(keys) as Dictionary<string, string>;
            }
            return null;
        }

        public string Any(ResolveBaseUrl req)
        {
            return Any(req.ConvertTo<ResolveNodesForRequest>())?.First().Value;
        }
        public List<RedisDiscoveryNodeInfo> Any(GetActiveNodes req)
        {
            return Redis.GetAll<RedisDiscoveryNodeInfo>(Redis.GetKeysByPattern("{0}:node:*".Fmt(HostContext.GetPlugin<RedisServiceDiscoveryFeature>().RedisPrefix))).Values.ToList();
        }

        public List<RedisHostMasterInfo> Any(GetActiveHosts req)
        {
            return Redis.GetAll<RedisHostMasterInfo>(Redis.GetKeysByPattern("{0}:host:*".Fmt(HostContext.GetPlugin<RedisServiceDiscoveryFeature>().RedisPrefix))).Values.ToList();
        }
    }

    public class RedisServiceDiscoveryGateway : ServiceGatewayFactoryBase
    {
        public override IServiceGateway GetGateway(Type requestType)
        {
            if (HostContext.Metadata.RequestTypes.Contains(requestType))
                return localGateway;
            var baseUrl = HostContext.GetPlugin<RedisServiceDiscoveryFeature>().ResolveBaseUrl(requestType);
            if (baseUrl.IsEmpty())
                throw new Exception("Cannot resolve request type to local or remote service endpoint.");
            return (IServiceGateway)new JsonServiceClient(baseUrl);
        }
    }
}

