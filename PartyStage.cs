using HarmonyLib;
using UnityEngine;

namespace PartyTime
{
    internal struct PartyStage
    {
        private const float OpenAirHeight = 8f;
        private const float MaxLookUp = 30f;
        private const float RigAboveFloor = 6.5f;
        private const float RigBelowCeiling = 1f;
        private const float MinRigAboveFloor = 2.5f;
        private const float MinRigBelowCeiling = 0.5f;

        private static readonly AccessTools.FieldRef<WalkNWashSceneDescription, WalkNWashSceneDescription.SceneDescription> SceneDescription =
            AccessTools.FieldRefAccess<WalkNWashSceneDescription, WalkNWashSceneDescription.SceneDescription>("sceneDescriptor");

        public Vector3 Floor;
        public float Ceiling;

        public float RigHeight
        {
            get { return Mathf.Max(Mathf.Min(Ceiling - RigBelowCeiling, Floor.y + RigAboveFloor), Mathf.Min(Floor.y + MinRigAboveFloor, Ceiling - MinRigBelowCeiling)); }
        }

        public static PartyStage Find(AudioSource music)
        {
            var stage = new PartyStage { Floor = music != null ? music.transform.position : Vector3.zero };
            var description = Object.FindFirstObjectByType<WalkNWashSceneDescription>();
            var path = description != null ? SceneDescription(description).windowToWashWaypoints : null;
            if (path != null && path.Count > 0 && path[path.Count - 1] != null) stage.Floor = path[path.Count - 1].position;

            stage.Ceiling = stage.Floor.y + OpenAirHeight;
            float nearest = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(stage.Floor + Vector3.up * 0.5f, Vector3.up, MaxLookUp, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider.GetComponentInParent<WalkNWashDragonDescriptor>() != null || hit.collider.GetComponentInParent<PlayerController>() != null) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                stage.Ceiling = hit.point.y;
            }
            return stage;
        }
    }
}
