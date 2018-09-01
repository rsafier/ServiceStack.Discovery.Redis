using common;
using Funq;
using ServiceStack;
using ServiceStack.DataAnnotations;
using ServiceStack.Discovery.Redis;
using ServiceStack.Messaging.Redis;
using ServiceStack.Redis;
using ServiceStack.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TestService2;

namespace TestService1
{
    public class AppHost : AppHostHttpListenerPoolBase
    {
        public AppHost() : base("TestService1", typeof(AppHost).Assembly)
        { }
        private string HostAt;
        public AppHost(string hostAt) : this()
        {
            HostAt = hostAt;
        }

        public override void Configure(Container container)
        {
            container.Register<IRedisClientsManager>(new RedisManagerPool(AppSettings.Get("RedisServer", "localhost:6379"), new RedisPoolConfig { MaxPoolSize = 100, }));
            SetConfig(new HostConfig
            {
                WebHostUrl = HostAt.Replace("*", Environment.MachineName),
            });
            LoadPlugin(new RedisServiceDiscoveryFeature()
            {
                ExcludedTypes = new HashSet<Type> { typeof(ExcludedServiceByHashset) },
                //SetServiceGateway = (baseUrl, requestType) => new JsonServiceClient(baseUrl) { UserAgent = "Custom User Agent" },
                NeverRunViaLocalGateway = new HashSet<Type> { typeof(Echo) }
            });

        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
                return;
            var app = new AppHost(args[0]).Init().Start(args[0]);
            $"Listening on {args[0]}".PrintDump();

            while (true)
            {
                Task.Delay(1000).Wait();
                HostContext.AppHost.ExecuteService(new Service1CallsService2() { From = "Service1" }).PrintDump();

                HostContext.AppHost.ExecuteService(new CallEcho() { Input = "Mary had a little lamb" }).PrintDump();
            }
        }
    }

    public class TestService : Service
    {

        /// <summary>
        /// Makes outbound call to Service2
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public object Any(Service1CallsService2 req)
        {
            return Gateway.Send(req.ConvertTo<Service2External>());
        }

        /// <summary>
        /// Receives inbound call from Service 2
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public object Any(Service1External req)
        {
            return $"Service1 Received from: {req.From}";
        }

        public void Any(ExcludedServiceByHashset req)
        {
            "ExcludedServiceByHashset called.".Print();
        }

        public string Any(Echo req) => $"{HostContext.ServiceName} is echoing {req.Input}";
        public string Any(CallEcho req) => Gateway.Send(req.ConvertTo<Echo>()); //This should not call the local
        
}


    public class Service1CallsService2 : IReturn<string>
    {
        public string From { get; set; }
    }

    public class Service1External : IReturn<string>
    {
        public string From { get; set; }
    }

    public class ExcludedServiceByHashset : IReturnVoid
    { }  

    [Restrict(AccessTo = RequestAttributes.InProcess)]
    public class CallEcho
    {
        public string Input { get; set; }
    }

}

namespace common
{
    [Exclude(Feature.ServiceDiscovery)]
    public class Echo : IReturn<string>
    {
        public string Input { get; set; }
    }
}