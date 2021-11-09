using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TacoLib.Gw2MumbleLib;

namespace TacoCli
{
    class Program
    {
        static void Main(string[] args)
        {
            Gw2Data data = null;
            while (true)
            {
                Thread.Sleep(1);
                var ndata = TacoLib.TacoLib.GetGw2Data();
                if (ndata.Equals(data))
                {
                    continue;
                }
                Console.WriteLine(JsonConvert.SerializeObject(data));
                data = ndata;
                Thread.Sleep(5);
            }
        }
    }
}
