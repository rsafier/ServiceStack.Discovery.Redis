## ServiceStack.Discovery.Redis
[![Build status](https://ci.appveyor.com/api/projects/status/github/rsafier/ServiceStack.Discovery.Redis?branch=master&svg=true)](https://ci.appveyor.com/project/rsafier/servicestack-discovery-redis)
[![NuGet version](https://badge.fury.io/nu/ServiceStack.Discovery.Redis.svg)](https://badge.fury.io/nu/ServiceStack.Discovery.Redis)

A plugin for [ServiceStack](https://servicestack.net/) that provides transparent service discovery using via a simple [Redis](http://redis.io)-backed datastore.

This enables your servicestack instances to call one another, without either knowing where the other is, based solely on a copy of the `RequestDTO` type. Your services will not need to take any dependencies on each other and as you deploy updates to your services they will automatically be registered and used without reconfiguing the existing services.

Additional basic hooks allow additional services to be built that can advertise there presence at the Host and Node level via a Meta dictonary which gets updated every node refresh period.
### Quick Start
Setup AppHost
```c#
public override void Configure(Container container)
{
    container.Register<IRedisClientsManager>(new RedisManagerPool("localhost:6379", new RedisPoolConfig { MaxPoolSize = 100, }));
    SetConfig(new HostConfig
    {
        WebHostUrl = "http://localhost:9999/"
    });
    Plugins.Add(new RedisServiceDiscoveryFeature());
}
```
To call external services, you call the Gateway and let it handle the routing for you.
```c#
public class MyService : Service
{
    public void Any(RequestDTO dto)
    {
        // If the gateway detects the type is locally served by the AppHost instance
        // the call will be functionally equilevent to calling HostContext.AppHost.ExecuteService(req) directly
        var internalCall = Gateway.Send(new InternalDTO { ... });
        // The gateway will automatically route external requests to the correct service if the type is not local
        // and it can resolve the `baseUrl` for the external service.
        var externalCall = Gateway.Send(new ExternalDTO { ... });
        
        try 
        {
            var unregisteredExternalCall = Gateway.Send(new ExternalDTOWithNoActiveNodesOnline());
        }
        catch(RedisServiceDiscoveryGatewayException e) 
        {
           // If a DTO type is not local or resolvable by the Redis discovery process 
           // a RedisServiceDiscoveryGatewayException will be thrown
        }
    }
}
```

### Requirements / Notes
- Using ServiceStack version 4.0.55 or greater
- A common Redis instance that all nodes in your discovery cluster register in the IOC (`IRedisClientsManager`) prior to loading plugin
- Set `HostConfig.WebHostUrl` to a connectable BaseUrl that will be used 
- ServiceStack license is recommened. [Free Quota](https://servicestack.net/download#free-quotas) limitation of 6000 Redis requests/hr could easily be exceeded, depending on the Node refresh period and number of exposed DTOs.
- DTOs are registered with their full type name (e.g. ServiceStack.Discovery.Redis.GetServiceRequestTypes). If you are importing your types via `Add Service Reference` and overriding your namespace you will run into issues.
- Services can be excluded from automatic registration by adding an `[ExcludeServiceDiscovery()]` attribute to the DTO
- `ResolveBaseUrl` is using a very simple policy of taking the `First()` Node matching the requested type. Additional criteria could be used by looking up NodeId details. (e.g. sort by lowest average load,uptime, etc.)


### Redis Key Structure
- **{RedisPrefix}:hosts:lastseen** - Hashset  of HostName key UnixDateTime of last update value
- **{RedisPrefix}:host:{HostName}** - Key  containing `RedisHostMasterInfo`
- **{RedisPrefix}:node:{HostName}:{ServiceName}:{NodeId}** - Key containing `RedisDiscoveryNodeInfo`
- **{RedisPrefix}:req:{FullTypeName}:{NodeId}** - Key containing baseUrl for FullType @ NodeId
- The default `RedisPrefix = "rsd"`.
- Keys will all have TTL set to `NodeTimeoutPeriod`

![Screen shot of test apps](images/SampleScreenshot.png)]

####Process details
On ServiceStack completing initialization `ServiceStack.Discovery.Redis` will start a periodic timer to refresh the node state every `NodeRefreshPeriod`. Each instance of AppHost will have a new `Guid` generated on startup as `NodeId` to ensure complete uniqueness. 

On each timer event, the exposed request types are updated, as well as local node `RedisDiscoveryNodeInfo`. Custom actions can be triggered on refresh by registering in `OnNodeRefreshActions`

If the `RedisDiscoveryRoles.CanHostMaster` role is set (default, unless removed from `Config.Roles` list) it will check is a HostName key already exists. If it does, it will attempt to set a key for the HostName, effectively acting as a lock. If the key is obtained then that `NodeId` will gain the `RedisDiscoveryRoles.HostMaster`. If the Node is `RedisDiscoveryRoles.HostMaster` then it will update it's `RedisHostMasterInfo` record and call custom `OnHostRefreshActions`

