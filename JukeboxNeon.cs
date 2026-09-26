using UnityEngine;

namespace PartyTime
{
    internal sealed class JukeboxNeon
    {
        private static readonly int EmissiveTint = Shader.PropertyToID("_EmissiveTint");

        private readonly Renderer _renderer;
        private readonly Color _materialTint;
        private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private Color _tint;
        private Color? _written;

        private JukeboxNeon(Renderer renderer, Color materialTint)
        {
            _renderer = renderer;
            _materialTint = materialTint;
            _tint = materialTint;
        }

        public static JukeboxNeon Find()
        {
            var jukebox = Object.FindFirstObjectByType<InteractableJukebox>();
            if (jukebox == null) return null;
            var model = jukebox.transform.parent != null ? jukebox.transform.parent : jukebox.transform;
            foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            {
                var material = renderer.sharedMaterial;
                if (material != null && material.HasProperty(EmissiveTint) && material.GetColor(EmissiveTint).maxColorComponent > 0.01f)
                    return new JukeboxNeon(renderer, material.GetColor(EmissiveTint));
            }
            return null;
        }

        public void Apply(float boost)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_block);
            var current = _block.HasColor(EmissiveTint) ? _block.GetColor(EmissiveTint) : _materialTint;
            if (!(_written.HasValue && current == _written.Value)) _tint = current;
            var boosted = _tint * (1f + boost);
            boosted.a = _tint.a;
            _block.SetColor(EmissiveTint, boosted);
            _renderer.SetPropertyBlock(_block);
            _written = boosted;
        }

        public void Restore()
        {
            if (_renderer == null || !_written.HasValue) return;
            _renderer.GetPropertyBlock(_block);
            _block.SetColor(EmissiveTint, _tint);
            _renderer.SetPropertyBlock(_block);
            _written = null;
        }
    }
}
