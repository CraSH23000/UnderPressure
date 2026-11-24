using UnityEngine;

namespace UnderPressure
{
    /// <summary>
    /// Cartoon debris: flies on a ballistic arc with spin, bounces once or twice on the floor, then settles flat.
    /// No physics engine involved, so it never shoves critters or pieces around.
    /// </summary>
    public class Tossed : MonoBehaviour
    {
        Vector3 vel, spin;
        float floorY;
        bool flipLanding;

        /// <summary>'spin' is in degrees per second (world axes). 'restHeight' is how high its pivot sits when it lies flat.
        /// 'canFlip' lets it land upside down (not for things with a knob on top).</summary>
        public static Tossed Launch(GameObject go, Vector3 velocity, Vector3 spin, float restHeight, bool canFlip = true)
        {
            var t = go.AddComponent<Tossed>();
            t.vel = velocity;
            t.spin = spin;
            t.floorY = restHeight;
            t.flipLanding = canFlip && Random.value < 0.5f;
            return t;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            vel += Physics.gravity * 1.6f * dt; // a bit floaty-heavy, cartoon style
            var p = transform.position + vel * dt;
            transform.Rotate(spin * dt, Space.World);
            if (p.y <= floorY && vel.y < 0f)
            {
                p.y = floorY;
                if (vel.y < -2.5f)
                {
                    vel = new Vector3(vel.x * 0.5f, -vel.y * 0.3f, vel.z * 0.5f); // bounce
                    spin *= 0.4f;
                }
                else
                {
                    // settle lying flat, keeping its heading
                    float yaw = transform.eulerAngles.y;
                    transform.rotation = Quaternion.Euler(flipLanding ? 180f : 0f, yaw, 0f);
                    transform.position = p;
                    enabled = false;
                    return;
                }
            }
            transform.position = p;
        }
    }
}
