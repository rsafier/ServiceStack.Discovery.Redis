## ServiceStack.Discovery.Redis

A plugin for [ServiceStack](https://servicestack.net/) that provides transparent service discovery using via a simple [Redis](http://redis.io)-backed datastore.

Basic hooks allow additional services to be built that can advertise there presense at the Host and Node level via Meta dictonary which gets updated every node refresh period.
### Redis Key Structure
- **{RedisPrefix}:hosts:lastseen** - Hashset  of HostName key UnixDateTime of last update value
- **{RedisPrefix}:host:{HostName}** - Key  containing `RedisHostMasterInfo`
- **{RedisPrefix}:node:{HostName}:{ServiceName}:{NodeId}** - Key containing `RedisDiscoveryNodeInfo`
- **{RedisPrefix}:req:{FullTypeName}:{NodeId}** - Key containing baseUrl for FullType @ NodeId
- The default `RedisPrefix = "rsd"`.
- Keys will all have TTL set to `NodeTimeoutPeriod`

On ServiceStack completing initialization `ServiceStack.Discovery.Redis` will start a periodic timer to refresh the node state every `NodeRefreshPeriod`. Each instance of AppHost will have a new `Guid` generated on startup as `NodeId` to ensure complete uniqueness. 

On each timer event, the exposed request types are updated, as well as local node `RedisDiscoveryNodeInfo`. Custom actions can be triggered on refresh by registering in `OnNodeRefreshActions`

If the `RedisDiscoveryRoles.CanHostMaster` role is set (default, unless removed from `Config.Roles` list) it will check is a HostName key already exists. If it does, it will attempt to set a key for the HostName, effectively acting as a lock. If the key is obtained then that `NodeId` will gain the `RedisDiscoveryRoles.HostMaster`. If the Node is `RedisDiscoveryRoles.HostMaster` then it will update it's `RedisHostMasterInfo` record and call custom `OnHostRefreshActions`

NOTE: This plugin is designed to run under a licensed copy of ServiceStack, this can easily generate enough Redis requests to blow thru the 6000 Redis requests/hr limit of the free quota fairly quickly depending on refresh period and the # of types being exposed.
