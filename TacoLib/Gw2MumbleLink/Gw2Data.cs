using System;
using System.Collections.Generic;
using System.Text;

namespace TacoLib.Gw2MumbleLib
{
    public class Gw2Data
    {
        // Advances every frame while the game runs, including when the player stands still, and
        // stops during loading screens (GW2 doesn't update MumbleLink then). Not part of Equals:
        // the position client watches it and sends loading screen events instead.
        public uint uiTick { get; set; }
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
