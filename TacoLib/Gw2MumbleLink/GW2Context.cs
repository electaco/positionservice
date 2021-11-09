using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TacoLib.Gw2MumbleLib
{
    [Flags]
    public enum UiState
    {
        MapOpen = 1,
        CompassTopRight = 2,
        CompassRotationEnabled = 4,
        GameHasFocus = 8,
        CompetetiveMode = 16,
        TextBoxInFocus = 32
    }

    public enum Mount
    {
        None,
        Jackal,
        Griffon,
        Springer,
        Skimmer,
        Raptor,
        RollerBeetle,
        Warclaw,
        Skyscale,
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct GW2Context
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 28)]
        public readonly byte[] ServerAddress;

        public uint MapId { get; }
        public uint MapType { get; }
        public uint ShardId { get; }
        public uint Instance { get; }
        public uint BuildId { get; }
        public UiState UiState { get; }
        public UInt16 CompassWidth { get; }
        public UInt16 CompassHeight { get; }
        public float CompassRotation { get; }
        public float MapPlayerX { get; } // continentCoords
        public float MapPlayerY { get; } // continentCoords
        public float MapCenterX { get; } // continentCoords
        public float MapCenterY { get; } // continentCoords
        public float MapScale { get; }
        public uint ProcessId { get; }
        public byte MountIndex { get; }

        public string[] UiFlags => GetUiFlags();

        private string[] GetUiFlags()
        {
            var uistate = this.UiState;
            return Enum.GetValues(typeof(UiState)).Cast<UiState>().Where(r => (uistate & r) == r).Select(r => r.ToString()).ToArray();
        }
    }
}