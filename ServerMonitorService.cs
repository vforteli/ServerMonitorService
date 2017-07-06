using FlexinetsDBEF;
using log4net;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace ServerMonitorService
{
    public partial class ServerMonitorService : ServiceBase
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(ServerMonitorService));
        private Timer pingTimer;
        private List<String> _ipAddresses;
        private FlexinetsEntitiesFactory _contextFactory;


        public ServerMonitorService()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            log4net.Config.XmlConfigurator.Configure();
            _contextFactory = new FlexinetsEntitiesFactory(ConfigurationManager.AppSettings["SQLConnectionString"]);
            _ipAddresses = ConfigurationManager.AppSettings["PingAddresses"].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            pingTimer = new Timer(PingRadiusServer, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
        }

        protected override void OnStop()
        {
            pingTimer.Dispose();
        }


        public void PingRadiusServer(object state)
        {
            try
            {
                using (var db = _contextFactory.GetContext())
                {
                    _ipAddresses = db.FL1Settings.SingleOrDefault(o => o.Name == "PingIpAddresses").Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }
            catch (Exception ex)
            {
                _log.Error("Unable to refresh ip list, using old values", ex);
            }

            Parallel.ForEach(_ipAddresses, ipAddress =>
            {
                try
                {

                    var reply = SendPing(ipAddress, 5000, 1);
                    var message = $"Ping from {Environment.MachineName} to {ipAddress} response {reply?.Status} in {reply?.RoundtripTime}ms";
                    _log.Debug(message);
                    using (var db = _contextFactory.GetContext())
                    {
                        db.ServerMonitorLogs.Add(new ServerMonitorLog
                        {
                            EventId = 1,
                            EventTimestamp = DateTime.UtcNow,
                            MachineName = Environment.MachineName,
                            Message = message,
                            Status = reply.Status.ToString(),
                            Target = ipAddress
                        });
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Something went wrong with ping", ex);
                }
            });
        }


        /// <summary>
        /// Ping an ip address
        /// </summary>
        /// <param name="ipAddress"></param>
        /// <param name="timeout"></param>
        /// <param name="retries"></param>
        /// <returns></returns>
        private PingReply SendPing(String ipAddress, Int32 timeout, Int32 retries = 0)
        {
            var ping = new Ping();
            PingReply reply = null;
            for (Int32 attempt = 0; attempt <= retries; attempt++)
            {
                reply = ping.Send(ipAddress, 5000);
                if (reply.Status == IPStatus.Success)
                {
                    break;
                }
            }
            return reply;
        }        
    }
}