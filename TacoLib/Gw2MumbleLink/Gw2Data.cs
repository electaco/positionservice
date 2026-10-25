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
        // When the service first saw this game frame (uiTick change), in milliseconds on the
        // service's own monotonic clock. Lets the overlay time samples without the delays of
        // sending them. Not part of Equals.
        public double sampleTime { get; set; }
        public GW2Link.Coordinates coordinates { get; set; }
        public GW2Identity identity { get; set; }
        public GW2Context context { get; set; }

        public override bool Equals(object obj)
        {
            var d = obj as Gw2Data;
            if (d != null)
            {
                // The map view too: panning or zooming the open map changes nothing else, and the
                // overlay draws markers on the map from it.
                return d.coordinates.Equals(coordinates) && context.UiState == d.context.UiState
                    && context.MapCenterX == d.context.MapCenterX && context.MapCenterY == d.context.MapCenterY
                    && context.MapScale == d.context.MapScale;
            }
            return false;
        }
    }
}
