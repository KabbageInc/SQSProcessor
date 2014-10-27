using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace ProcessServer
{
    public class Service : ServiceBase
    {
        public Service()
        {
            ServiceName = "ProcessServer";
        }

        protected override void OnStart(string[] args)
        {
            ProcessServer.Start(args);
        }

        protected override void OnStop()
        {
            ProcessServer.Stop();
        }
    }
}