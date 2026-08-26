using System.Collections.Generic;
using FaceSlapper.Core;
using UnityEngine;

namespace FaceSlapper.Battle
{
    /// <summary>
    /// 命中/眩晕反馈组件（全局唯一，挂在 GameManager 上）：
    /// 订阅全端广播的 Fx 事件，在命中位置播放音效（AudioSource.PlayClipAtPoint）
    /// 与星星爆裂（贴花 Quad 锥形飞散 + 旋转 + 缩小，生命周期结束自动回收）。
    /// 资源经 Resources 运行时加载，无需在场景中手动摆放。
    /// </summary>
    public class HitFeedbackComponent : MonoBehaviour, IGameComponent, IUpdatable
    {
        [Header("音效")]
        [SerializeField] private float _hitVolume = 0.9f;
        [SerializeField] private float _bonkVolume = 1f;

        [Header("星星爆裂")]
        [SerializeField] private int _burstCount = 8;
        [SerializeField] private float _burstSpeed = 5f;
        [SerializeField] private float _burstLife = 0.7f;
        [SerializeField] private float _starSize = 0.14f;

        private AudioClip _hitClip;
        private AudioClip _bonkClip;
        private Material _starMaterial;

        private class BurstStar
        {
            public Transform Transform;
            public Vector3 Velocity;
            public float Spin;
        }

        private class Burst
        {
            public readonly List<BurstStar> Stars = new List<BurstStar>(8);
            public float Age;
        }

        private readonly List<Burst> _bursts = new List<Burst>(8);

        public void OnInit()
        {
            _hitClip = Resources.Load<AudioClip>("Audio/SfxHit");
            _bonkClip = Resources.Load<AudioClip>("Audio/SfxBonk");
            if (_hitClip == null)
                Debug.LogWarning("[Feedback] 缺少 Resources/Audio/SfxHit.wav（运行 Tools/GenerateSfx.py 生成）。");
            if (_bonkClip == null)
                Debug.LogWarning("[Feedback] 缺少 Resources/Audio/SfxBonk.wav（运行 Tools/GenerateSfx.py 生成）。");

            // 星星材质运行时构建（贴图复用 ArtSetup 生成到 Resources 的 StarTex）。
            Texture2D starTex = Resources.Load<Texture2D>("Art/Textures/StarTex");
            Shader decal = Shader.Find("FaceSlapper/ToonDecal");
            if (starTex != null && decal != null)
            {
                _starMaterial = new Material(decal);
                _starMaterial.SetTexture("_BaseMap", starTex);
                _starMaterial.SetColor("_BaseColor", new Color(1f, 0.9f, 0.2f));
            }
            else
            {
                Debug.LogWarning("[Feedback] 缺少星星贴图或 ToonDecal Shader（运行 FaceSlapper/Generate Art Assets 生成）。");
            }

            EventBus.Subscribe<PlayerHitFxEvent>(OnHitFx);
            EventBus.Subscribe<PlayerStunFxEvent>(OnStunFx);
        }

        public void OnShutdown()
        {
            EventBus.Unsubscribe<PlayerHitFxEvent>(OnHitFx);
            EventBus.Unsubscribe<PlayerStunFxEvent>(OnStunFx);

            foreach (Burst burst in _bursts)
                foreach (BurstStar star in burst.Stars)
                    if (star.Transform != null) Destroy(star.Transform.gameObject);
            _bursts.Clear();

            if (_starMaterial != null) Destroy(_starMaterial);
        }

        private void OnHitFx(PlayerHitFxEvent e)
        {
            // 音量随力度缩放，重击更响。
            PlayClip(_hitClip, e.Position, _hitVolume * Mathf.Clamp01(e.Force / 12f));
            SpawnBurst(e.Position + Vector3.up, e.Direction);
        }

        private void OnStunFx(PlayerStunFxEvent e)
        {
            PlayClip(_bonkClip, e.Position, _bonkVolume);
            SpawnBurst(e.Position + Vector3.up * 1.2f, Vector3.up);
        }

        private static void PlayClip(AudioClip clip, Vector3 position, float volume)
        {
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position, volume);
        }

        /// <summary>在指定位置爆开一圈星星：沿命中方向锥形飞散。</summary>
        private void SpawnBurst(Vector3 center, Vector3 direction)
        {
            if (_starMaterial == null) return;

            var burst = new Burst();
            for (int i = 0; i < _burstCount; i++)
            {
                GameObject star = GameObject.CreatePrimitive(PrimitiveType.Quad);
                star.name = "HitStar";
                Destroy(star.GetComponent<Collider>());
                star.transform.position = center;
                star.transform.localScale = Vector3.one * _starSize;
                star.transform.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                star.GetComponent<Renderer>().sharedMaterial = _starMaterial;

                Vector3 spread = direction + Random.insideUnitSphere * 0.9f + Vector3.up * 0.6f;
                burst.Stars.Add(new BurstStar
                {
                    Transform = star.transform,
                    Velocity = spread.normalized * (_burstSpeed * Random.Range(0.6f, 1.2f)),
                    Spin = Random.Range(-540f, 540f),
                });
            }
            _bursts.Add(burst);
        }

        public void OnUpdate(float deltaTime)
        {
            for (int i = _bursts.Count - 1; i >= 0; i--)
            {
                Burst burst = _bursts[i];
                burst.Age += deltaTime;
                float t = burst.Age / _burstLife;
                if (t >= 1f)
                {
                    foreach (BurstStar star in burst.Stars)
                        if (star.Transform != null) Destroy(star.Transform.gameObject);
                    _bursts.RemoveAt(i);
                    continue;
                }

                foreach (BurstStar star in burst.Stars)
                {
                    if (star.Transform == null) continue;
                    star.Velocity += Vector3.down * (9.8f * deltaTime); // 重力下坠
                    star.Transform.position += star.Velocity * deltaTime;
                    star.Transform.Rotate(0f, 0f, star.Spin * deltaTime);
                    star.Transform.localScale = Vector3.one * (_starSize * (1f - t * 0.7f));
                }
            }
        }
    }
}
