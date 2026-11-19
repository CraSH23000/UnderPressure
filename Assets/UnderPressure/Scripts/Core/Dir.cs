using UnityEngine;

namespace UnderPressure
{
    /// <summary>Grid directions. N = +Z, E = +X, S = -Z, W = -X (viewed from above).</summary>
    public enum Dir { N = 0, E = 1, S = 2, W = 3 }

    public static class DirUtil
    {
        public static readonly Dir[] All = { Dir.N, Dir.E, Dir.S, Dir.W };

        /// <summary>Rotate clockwise (viewed from above) by 90° steps. Matches Quaternion.Euler(0, 90 * steps, 0).</summary>
        public static Dir Rotate(Dir d, int steps) => (Dir)((((int)d + steps) % 4 + 4) % 4);

        public static Dir Opposite(Dir d) => Rotate(d, 2);

        public static Vector2Int Offset(Dir d)
        {
            switch (d)
            {
                case Dir.N: return new Vector2Int(0, 1);
                case Dir.E: return new Vector2Int(1, 0);
                case Dir.S: return new Vector2Int(0, -1);
                default: return new Vector2Int(-1, 0);
            }
        }

        public static Vector3 ToVector3(Dir d)
        {
            var o = Offset(d);
            return new Vector3(o.x, 0f, o.y);
        }

        /// <summary>Snap an XZ vector to the dominant cardinal direction.</summary>
        public static Dir FromVector(Vector3 v)
        {
            if (Mathf.Abs(v.x) > Mathf.Abs(v.z)) return v.x > 0 ? Dir.E : Dir.W;
            return v.z > 0 ? Dir.N : Dir.S;
        }
    }

    /// <summary>Unnamed user layers used by the game. Names are optional; indices are what matter.</summary>
    public static class Layers
    {
        public const int Default = 0;
        public const int PlacedPipes = 8;
        public const int PlayerClimbing = 9;
        public const int LoosePieces = 10;
        public const int PlacedWalkable = 11; // installed crosses: low enough to walk straight over
        public const int Player = 12;

        public static void SetupCollisionMatrix()
        {
            // Walking player bumps into placed pipes, but walks *through* loose pieces (they slow you down instead).
            Physics.IgnoreLayerCollision(Player, LoosePieces, true);
            Physics.IgnoreLayerCollision(Player, PlacedPipes, false);
            // Climbing/vaulting player passes over placed pipes.
            Physics.IgnoreLayerCollision(PlayerClimbing, PlacedPipes, true);
            Physics.IgnoreLayerCollision(PlayerClimbing, LoosePieces, true);
            Physics.IgnoreLayerCollision(Player, PlayerClimbing, true);
            Physics.IgnoreLayerCollision(Player, PlacedWalkable, true);
            Physics.IgnoreLayerCollision(PlayerClimbing, PlacedWalkable, true);
        }

        public static void SetRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetRecursive(c.gameObject, layer);
        }
    }
}
