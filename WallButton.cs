using System;
using com.gatordragongames.washnwalk.tools;
using FluidRenderingForGames;
using UnityEngine;

namespace PartyTime
{
    internal sealed class WallButton : Interactable
    {
        private const float CooldownSeconds = 0.4f;
        private const float MaxReach = 3f;
        private const float EyeClearance = 0.1f;
        private const int SolidLayers = 385;

        private float _lastPress = float.NegativeInfinity;

        public Action Pressed;

        public float LastPress
        {
            get { return _lastPress; }
        }

        public override bool interactsWithHand
        {
            get { return true; }
        }

        protected override bool CanInteract(Tool tool)
        {
            return IsReachable();
        }

        public override void Interact(Tool tool)
        {
            if (Time.unscaledTime - _lastPress < CooldownSeconds) return;
            _lastPress = Time.unscaledTime;
            Pressed?.Invoke();
        }

        public bool IsReachable()
        {
            var eye = LookController.GetLookPosition();
            var toButton = transform.position - eye;
            if (Vector3.Dot(toButton, transform.forward) >= 0f) return false;
            float distance = toButton.magnitude;
            if (distance > MaxReach) return false;
            if (distance <= EyeClearance) return true;
            var direction = toButton / distance;
            if (!Physics.Raycast(eye + direction * EyeClearance, direction, out var hit, distance - EyeClearance, SolidLayers, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<WallButton>() != null;
        }
    }

    internal sealed class WallButtonHitbox : HitboxTrigger
    {
        public WallButton Button;

        public override void Hit(FluidParticleSystemSettings fluid, HitType type)
        {
            base.Hit(fluid, type);
            if (type == HitType.Plap && Button != null && Button.IsReachable()) Button.Interact(ToolManager.GetCurrentTool());
        }
    }
}
