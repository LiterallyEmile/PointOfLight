using UnityEngine;

namespace PointOfLight.Cuphead
{
    /// Reads Ori's buttons straight from Unity's input, alongside Cuphead's own
    /// (Rewired) input, so movement, jump, dash and parry stay Cuphead's.
    internal sealed class OriInput
    {
        public bool AttackDown, AttackHeld, AbilityDown, CycleDown, BashDown, BashHeld, BashUp, ToggleDown;
        public Vector2 Dir;          // raw stick/arrow direction, each axis -1/0/1

        bool _axesBroken;

        public void Poll(PluginConfig c)
        {
            AttackDown  = Input.GetKeyDown(c.AttackKey.Value)  || Input.GetKeyDown(c.AttackPad.Value);
            AttackHeld  = Input.GetKey(c.AttackKey.Value)      || Input.GetKey(c.AttackPad.Value);
            AbilityDown = Input.GetKeyDown(c.AbilityKey.Value) || Input.GetKeyDown(c.AbilityPad.Value);
            CycleDown   = Input.GetKeyDown(c.CycleKey.Value)   || Input.GetKeyDown(c.CyclePad.Value);
            BashDown    = Input.GetKeyDown(c.BashKey.Value)    || Input.GetKeyDown(c.BashPad.Value);
            BashHeld    = Input.GetKey(c.BashKey.Value)        || Input.GetKey(c.BashPad.Value);
            BashUp      = Input.GetKeyUp(c.BashKey.Value)      || Input.GetKeyUp(c.BashPad.Value);
            ToggleDown  = Input.GetKeyDown(c.Toggle.Value);

            float x = 0f, y = 0f;
            if (Input.GetKey(c.Left.Value))  x -= 1f;
            if (Input.GetKey(c.Right.Value)) x += 1f;
            if (Input.GetKey(c.Down.Value))  y -= 1f;
            if (Input.GetKey(c.Up.Value))    y += 1f;
            if (x == 0f && y == 0f && !_axesBroken)
            {
                // Unity's default "Horizontal"/"Vertical" axes include the left stick.
                // A game can strip them from its Input Manager, so stop asking if they're gone.
                try
                {
                    float ax = Input.GetAxisRaw("Horizontal"), ay = Input.GetAxisRaw("Vertical");
                    if (Mathf.Abs(ax) > 0.5f) x = Mathf.Sign(ax);
                    if (Mathf.Abs(ay) > 0.5f) y = Mathf.Sign(ay);
                }
                catch { _axesBroken = true; }
            }
            Dir = new Vector2(x, y);
        }
    }
}
