using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using MumbleLink_CSharp;
using Newtonsoft.Json;
using System.Numerics;

namespace MumbleLink_CSharp_GW2
{
    public class GW2Link : MumbleLink
    {
        private const double MeterToInch = 39.3700787d;

        public struct Coordinates
        {
            public Vector3 playerPosition;
            public Vector3 cameraPosition;
            public Vector3 cameraAngle;
            public Vector3 playerViewAngle;
            public Vector3 playerTop;
            public Vector3 cameraTop;
            public int WorldId;
            public int MapId;
        }

        public Gw2Data getData()
        {
            return new Gw2Data
            {
                coordinates = GetCoordinates(),
                context = GetContext(),
                identity = GetIdentity()
            };
        }

        public GW2Context GetContext()
        {
            var l = Read();

            int size = Marshal.SizeOf(typeof(GW2Context));

            IntPtr ptr = Marshal.AllocHGlobal(size);

            Marshal.Copy(l.Context, 0, ptr, size);

            var result = (GW2Context)Marshal.PtrToStructure(ptr, typeof(GW2Context));

            Marshal.FreeHGlobal(ptr);

            return result;
        }



        public Coordinates GetCoordinates()
        {
            MumbleLinkedMemory l = Read();

            /* 
             * Note that the mumble coordinates differ from the actual in-game coordinates.
             * They are in the format x,z,y and z has been negated so that underwater is negative
             * rather than positive.
             * 
             * Coordinates are based on a central point (0,0), which may be the center of the zone, 
             * where traveling west is negative, east is positive, north is positive and south is negative.
             * 
             */

            var coord = new Coordinates()
            {
                playerPosition = new Vector3(l.FAvatarPosition[0], l.FAvatarPosition[1], l.FAvatarPosition[2]),
                cameraPosition = new Vector3(l.FCameraPosition[0], l.FCameraPosition[1], l.FCameraPosition[2]),
                cameraAngle = new Vector3(l.FCameraFront[0], l.FCameraFront[1], l.FCameraFront[2]),
                playerViewAngle= new Vector3(l.FAvatarFront[0], l.FAvatarFront[1], l.FAvatarFront[2]),
                playerTop = new Vector3(l.FAvatarTop[0], l.FAvatarTop[1], l.FAvatarTop[2]),
                cameraTop = new Vector3(l.FCameraTop[0], l.FCameraTop[1], l.FCameraTop[2]),
                WorldId = BitConverter.ToInt32(l.Context, 36),
                MapId = BitConverter.ToInt32(l.Context, 28)
            };
            return coord;
        }

        /// <summary>
        /// Returns the parsed Identitty Field
        /// </summary>
        /// <returns>GW2Identity Instance if succeeded, null if not</returns>
        public GW2Identity GetIdentity()
        {
            var identity = Read().Identity;

            var stop = Array.IndexOf(Read().Identity, '\0');

            unsafe
            {
                fixed (char* addr = identity)
                {
                    return JsonConvert.DeserializeObject<GW2Identity>(new String(addr));//Needs to use the array in char* form because when it changes size, GW2 does not clean all of the array, it just put \0 after the content
                }
            }
        }

    }
}
