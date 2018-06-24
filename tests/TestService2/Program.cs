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
using TestService1;

namespace TestService2
{
    public class AppHost : AppHostHttpListenerPoolBase
    {
        public AppHost() : base("TestService2", typeof(AppHost).Assembly)
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
                WebHostUrl = HostAt.Replace("*", Environment.MachineName)
            });
            LoadPlugin(new RedisServiceDiscoveryFeature());
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
                HostContext.AppHost.ExecuteService(new Service2CallsService1() { From = "Service2" }).PrintDump();
                HostContext.AppHost.ExecuteService(new Echo() { Input = "His fleece was white as snow" }).PrintDump();
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
        public object Any(Service2CallsService1 req)
        {
            return Gateway.Send(req.ConvertTo<Service1External>());
        }

        /// <summary>
        /// Receives inbound call from Service 2
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public object Any(Service2External req)
        {
            return $"Service2 Received from: {req.From}";
        }

       

          public string Any(Echo req) => $"{HostContext.ServiceName} is echoing {req.Input}";


    }


    public class Service2CallsService1 : IReturn<string>
    {
        public string From { get; set; }
    }


    public class Service2External : IReturn<string>
    {
        public string From { get; set; }
    }

}

namespace common
{
    public class Echo : IReturn<string>
    {
        public string Input { get; set; }
    }
}