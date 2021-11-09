using System;
using System.Collections.Generic;
using System.Text;

namespace TacoLib.Gw2MumbleLib
{
    public class Gw2Data
    {
        public GW2Link.Coordinates coordinates { get; set; }
        public GW2Identity identity { get; set; }
        public GW2Context context { get; set; }

        public override bool Equals(object obj)
        {
            var d = obj as Gw2Data;
            if (d != null)
            {
                return d.coordinates.Equals(coordinates) && context.UiState == d.context.UiState;
            }
            return false;
        }
    }
}
