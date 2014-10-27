using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using SimpleAWS;
using SimpleAWS.Models;
using SimpleAWS.Models.S3;
using SimpleAWS.Models.SQS;

namespace ProcessQueue
{
    public class ProcessQueue
    {
        private static int PROCESS_TASKS;
        private static string LOCAL_IP;

        private static string AWSSQSAccessKey = ConfigurationManager.AppSettings["AWSAccessKey"];
        private static string AWSSQSSecretKey = ConfigurationManager.AppSettings["AWSSecretKey"];

        private static IPEndPoint endpoint = new IPEndPoint(IPAddress.Parse(ConfigurationManager.AppSettings["ServerAddress"]), Int32.Parse(ConfigurationManager.AppSettings["ServerHost"]));
        
        private static SQSClient sqsClient = null;

        private static string queueUrl = ConfigurationManager.AppSettings["QueueUrl"];
        private static int updateInterval = Int32.Parse(ConfigurationManager.AppSettings["UpdateInterval"]);
        private static int delayedStartInterval = Int32.Parse(ConfigurationManager.AppSettings["DelayedStartInterval"]);
        private static int visibilityTimeout = Int32.Parse(ConfigurationManager.AppSettings["VisibilityTimeout"]);
        
        private static System.Timers.Timer delayedStart;
        private static System.Timers.Timer statusUpdater;
        private static System.Timers.Timer performanceCount;

        private static List<Thread> tasks = new List<Thread>();

        private static bool cancel = false;

        private static DateTime startTime;

        private static long performanceTotal = 0;
        private static float filesPerSecond = 0;

        private static void WriteError(string name, string message)
        {
            try
            {
                if (!EventLog.SourceExists(name))
                    EventLog.CreateEventSource(name, "Application");

                EventLog.WriteEntry(name, message, EventLogEntryType.Error);
            }
            catch { }
        }

        private static string GetIP()
        {
            string localIP = "?";
            IPHostEntry host = Dns.GetHostEntry(Dns.GetHostName());

            foreach (IPAddress ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    localIP = ip.ToString();
                    break;
                }
            }

            return localIP;
        }

        public static void Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = Int32.MaxValue;
            ServicePointManager.CheckCertificateRevocationList = false;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            int processTasks;

            if (args.Length != 1 || !Int32.TryParse(args[0], out processTasks))
                return;

            PROCESS_TASKS = processTasks;

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
            startTime = DateTime.Now;

            sqsClient = new SQSClient(AWSSQSAccessKey, AWSSQSSecretKey, 4);

            delayedStart = new System.Timers.Timer(delayedStartInterval);
            delayedStart.Elapsed += delayedStart_Elapsed;
            delayedStart.Start();

            var random = new Random();
            var addInterval = random.Next(updateInterval);

            statusUpdater = new System.Timers.Timer(updateInterval + addInterval);
            statusUpdater.Elapsed += statusUpdater_Elapsed;
            statusUpdater.Start();

            performanceCount = new System.Timers.Timer(60000);
            performanceCount.Elapsed += performanceCount_Elapsed;
            performanceCount.Start();
        }

        public static void Stop()
        {
            if (statusUpdater != null)
            {
                statusUpdater.Dispose();
                statusUpdater = null;
            }

            if (performanceCount != null)
            {
                performanceCount.Dispose();
                performanceCount = null;
            }

            if (delayedStart != null)
            {
                delayedStart.Dispose();
                delayedStart = null;
            }

            try
            {
                cancel = true;

                foreach (var t in tasks)
                    t.Join();
            }
            catch {}
        }

        private static void SendClientUpdate()
        {
            try
            {
                string message = System.Environment.MachineName + "|" + startTime.ToBinary() + "|" + filesPerSecond + "|" + LOCAL_IP;
                
                TcpClient client = new TcpClient();
                client.Connect(endpoint);

                if (client.Connected)
                {
                    byte[] data = System.Text.Encoding.ASCII.GetBytes(message);
                    var stream = client.GetStream();
                    stream.Write(data, 0, data.Length);
                    stream.Close();
                }

                client.Close();
            }
            catch (Exception exc)
            {
                WriteError("ProcessQueue Update", string.Format("Exception sending client update: {0}", exc.ToString()));
            }
        }

        private static void ProcessMessages()
        {
            do
            {
                try
                {
                    var response = sqsClient.ReceiveMessage(new ReceiveMessageRequest
                    {
                        MaxNumberOfMessages = 10,
                        QueueUrl = queueUrl,
                        VisibilityTimeout = visibilityTimeout,
                        WaitTimeSeconds = 20
                    });

                    if (response == null)
                        continue;

                    foreach (var message in response.ReceiveMessageResult.Messages)
                    {
                        if (cancel)
                            break;

                        if (message == null)
                            continue;

                        //do something with your message here
                    }
                }
                catch (Exception exc)
                {
                    WriteError("ProcessQueue SQS", string.Format("Exception pulling message: {0}", exc.ToString()));
                }
            }
            while (!cancel);
        }

        private static void delayedStart_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            delayedStart.Stop();

            LOCAL_IP = GetIP();

            for (int i = 0; i < PROCESS_TASKS; i++)
            {
                var thread = new Thread(ProcessMessages);
                tasks.Add(thread);
                thread.Start();
            }
        }

        private static void performanceCount_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            filesPerSecond = (float)performanceTotal / (float)60;
            Interlocked.Exchange(ref performanceTotal, 0);
        }

        private static void statusUpdater_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            SendClientUpdate();
        }
    }
}
