using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>Tiny service locator, filled by GameBootstrap and cleared on every (re)load.</summary>
    public static class Game
    {
        public static GameConfig Config;
        public static GridBoard Board;
        public static Conveyor Conveyor;
        public static BeltLever Lever;
        public static WallMonitor Monitor;
        public static readonly List<PlayerController> Players = new List<PlayerController>();
        public static FlowController Flow;
        public static GameManager Manager;
        public static Hud Hud;
        public static CameraRig Cam;
        public static GameBootstrap Bootstrap;
        /// <summary>Everything level-specific is parented here so a restart can just destroy it.</summary>
        public static Transform LevelRoot;

        public static void Clear()
        {
            Config = null; Board = null; Conveyor = null; Lever = null; Monitor = null; Players.Clear();
            Flow = null; Manager = null; Hud = null; LevelRoot = null;
            PipePiece.Loose.Clear();
            Closet.All.Clear();
            Progress.TakePending();
        }
    }
}
