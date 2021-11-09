using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace TacoLib.Gw2MumbleLib
{
    public class GW2Identity
    {

        public String Name { get; set; }
        public GW2Profession Profession { get; set; }
        public GW2Race Race { get; set; }
        public int MapId { get; set; }
        public int WorldId { get; set; }
        public int TeamColorId { get; set; }
        public Boolean IsCommander { get; set; }
        public float FovRadians { get; set; }
        public GW2UiSize UiSize { get; set; }
        public int Spec { get; set; }

        public float FovDegrees => (float)(180 / Math.PI * FovRadians);

        [JsonConstructor]
        public GW2Identity(string name, GW2Profession profession, GW2Race race, int map_id, int world_id, int team_color_id, bool commander, float fov, GW2UiSize uisz, int spec)
        {
            Spec = spec;
            Name = name;
            Profession = profession;
            Race = race;
            MapId = map_id;
            WorldId = world_id;
            TeamColorId = team_color_id;
            IsCommander = commander;
            FovRadians = fov;
            UiSize = uisz;
        }

    }


}
