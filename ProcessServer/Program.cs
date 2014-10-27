using ProcessServer;
using System;
using System.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Globalization;
using SimpleAWS;
using SimpleAWS.Models.EC2;

namespace ProcessServer
{
    public class ProcessServer
    {
        public static WebServer ws = null;
        public static TcpListener listener = null;
        public static List<Client> connections = new List<Client>();
        public static Dictionary<string, ClientUpdate> clients = new Dictionary<string, ClientUpdate>();
        
        private static string AWSEC2AccessKey = ConfigurationManager.AppSettings["AWSEC2AccessKey"];
        private static string AWSEC2SecretKey = ConfigurationManager.AppSettings["AWSEC2SecretKey"];
        private static string ImageId = ConfigurationManager.AppSettings["ImageId"];

        private static EC2Client ec2Client = null;

        private static void WriteError(string message)
        {
            if (!EventLog.SourceExists("ProcessServer"))
                EventLog.CreateEventSource("ProcessServer", "Application");

            EventLog.WriteEntry("ProcessServer", message, EventLogEntryType.Error);
        }

        public static void Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = Int32.MaxValue;
            ServicePointManager.CheckCertificateRevocationList = false;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            if (!Environment.UserInteractive)
            {
                using (var service = new Service())
                {
                    ServiceBase.Run(service);
                }
            }
            else
            {
                Start(args);

                Console.WriteLine("Press any key to stop...");
                Console.ReadKey(true);

                Stop();
            }
        }

        public static void Start(string[] args)
        {
            int webPort = Int32.Parse(ConfigurationManager.AppSettings["WebPort"]);
            int serverPort = Int32.Parse(ConfigurationManager.AppSettings["ServerPort"]);

            ec2Client = new EC2Client(AWSEC2AccessKey, AWSEC2SecretKey, 4);

            IPAddress address = null;
            IPHostEntry ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());

            foreach (var ip in ipHostInfo.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    address = ip;
                    break;
                }
            }

            if (address == null)
                return;

            ws = new WebServer(SendHttpResponse, string.Format("http://{0}:{1}/", address.ToString(), webPort));
            ws.Run();

            listener = new TcpListener(IPAddress.Any, serverPort);
            listener.Start();
            listener.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
        }

        public static void Stop()
        {
            if (ws != null)
                ws.Stop();

            listener.Stop();

            lock (connections)
            {
                foreach (var client in connections)
                {
                    client.NetworkStream.Close();
                    client.TcpClient.Close();
                }
            }
        }

        private static void AcceptTcpClientCallback(IAsyncResult result)
        {
            if (result == null)
                return;
            
            if (listener == null || listener.Server == null || !listener.Server.IsBound)
                return;

            TcpClient tcpClient = listener.EndAcceptTcpClient(result);
            byte[] buffer = new byte[tcpClient.ReceiveBufferSize];

            Client client = new Client(tcpClient, buffer);

            lock (connections)
            {
                connections.Add(client);
            }

            NetworkStream networkStream = client.NetworkStream;
            networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);

            listener.BeginAcceptTcpClient(AcceptTcpClientCallback, null);
        }

        private static void ReadCallback(IAsyncResult result)
        {
            if (!listener.Server.IsBound)
                return;

            NetworkStream networkStream = null;
            Client client = result.AsyncState as Client;

            if (client == null)
                return;

            int read = 0;
            try
            {
                networkStream = client.NetworkStream;
                read = networkStream.EndRead(result);
            }
            catch { read = 0; }

            if (read == 0)
            {
                lock (connections)
                {
                    client.NetworkStream.Close();
                    client.TcpClient.Close();

                    connections.Remove(client);

                    ProcessMessage(client.Message);
                    return;
                }
            }
            else
            {
                client.Message += System.Text.Encoding.ASCII.GetString(client.Buffer, 0, read);

                if (networkStream != null)
                    networkStream.BeginRead(client.Buffer, 0, client.Buffer.Length, ReadCallback, client);
            }
        }

        public static string SendHttpResponse(HttpListenerRequest request)
        {
            string htmlBody = "<HTML><BODY>";
            htmlBody += "<b>Client Server Processing</b>";

            var filter = new Filter();
            filter.Name = "image-id";
            filter.Values.Add(ImageId);

            List<string> runningAddresses = new List<string>();

            try
            {
                var instanceResponse = ec2Client.DescribeInstances(new DescribeInstancesRequest { Filters = new List<Filter> { filter } });
                var allInstances = instanceResponse.Reservations.SelectMany(x => x.Instances).Where(x => x.State.Name != InstanceStateName.terminated);
                runningAddresses = allInstances.Select(x => x.PrivateIpAddress).ToList();
            }
            catch { }

            try
            {
                htmlBody += "<br/>";
                htmlBody += "<hr/>";

                float filesPerSecond = clients.Values.Sum(x => x.FilesPerSecond);

                htmlBody += "<table>";
                htmlBody += "<tr><td align='right'>Current time:</td><td align='left'>" + DateTime.Now + "</td></tr>";
                htmlBody += "<tr><td align='right'>Files per second:</td><td align='left'>" + ((filesPerSecond > 0) ? filesPerSecond.ToString("#.##") : filesPerSecond.ToString()) + "</td></tr>";
                htmlBody += "<tr><td align='right'>Total clients:</td><td align='left'>" + clients.Count(x => (DateTime.Now - x.Value.UpdateTime).TotalSeconds < 120) + "</td></tr>";
                htmlBody += "</table>";

                htmlBody += "<hr/>";

                htmlBody += "<table style=\"border-collapse:collapse;\" border=\"1\" cellpadding=\"4\">";

                foreach (var update in clients.Values)
                {
                    var dead = (DateTime.Now - update.UpdateTime).TotalSeconds > 120;

                    if (dead && update.ClientName == "IP-0A05C955")
                        continue;

                    string data = string.Format("<td><b>Client:</b>{0}</td><td><b>MinutesRunning:</b>{1}</td><td><b>Speed:</b>{2}</td><td><b>IP:</b>{3}</td>",
                        update.ClientName,
                        (DateTime.Now - update.StartTime).TotalMinutes.ToString("#.##"),
                        ((update.FilesPerSecond > 0) ? update.FilesPerSecond.ToString("#.##") : update.FilesPerSecond.ToString()),
                        update.LocalIP);

                    if (dead && runningAddresses.Contains(update.LocalIP))
                        htmlBody += "<tr bgcolor=\"#FF0000\">" + data + "</tr>";
                    else if (dead)
                        htmlBody += "<tr bgcolor=\"#C0C0C0\">" + data + "</tr>";
                    else
                        htmlBody += "<tr bgcolor=\"#00FF00\">" + data + "</tr>";
                }

                htmlBody += "</table>";
            }
            catch (Exception exc)
            {
                WriteError(string.Format("Exception displaying output: {0}", exc.ToString()));
            }

            htmlBody += "</BODY></HTML>";

            return htmlBody;
        }

        public static void ProcessMessage(string data)
        {
            try
            {
                string[] messageSplit = data.Split('|');

                if (messageSplit.Length == 7)
                {
                    var update = new ClientUpdate
                    {
                        UpdateTime = DateTime.Now,
                        ClientName = messageSplit[0],
                        StartTime = DateTime.FromBinary(long.Parse(messageSplit[1])),
                        FilesPerSecond = float.Parse(messageSplit[2]),
                        LocalIP = messageSplit[3],
                    };

                    if (!clients.ContainsKey(messageSplit[0]))
                        clients.Add(messageSplit[0], update);
                    else
                        clients[messageSplit[0]] = update;
                }
            }
            catch (Exception exc)
            {
                WriteError(string.Format("Exception reading server message: {0}", exc.ToString()));
            }
        }
    }
}