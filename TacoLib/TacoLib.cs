using System.Collections.Generic;
using System.Numerics;
using TacoLib.Gw2MumbleLib;

namespace TacoLib
{
    public class TacoLib
    {
        public static GW2Link gw2Link = new GW2Link();

        public static Gw2Data GetGw2Data()
        {
            var gw2Data = gw2Link.getData();
            return gw2Data;
        }
    }
}
