using System;
using System.Collections.Generic;
using HorseRace.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace HorseRace.View
{
    /// <summary>
    /// 用 3D 模型（Resources 底下的 FBX／prefab）呈現的馬。
    ///
    /// - 朝向與大小：依設定轉向，並自動縮放到指定身長、腳底貼地、身體置中，換不同比例的模型不用改程式。
    /// - 顏色：材質名稱含指定關鍵字的部位（例如騎師服）染成該馬的代表色。
    /// - 動畫：用 Playables 直接播 FBX 裡的動畫片段，不需要在編輯器建 Animator Controller；
    ///   播放時間由程式手動推進並自行循環，所以片段在匯入時有沒有勾 Loop 都能用；
    ///   播放速度跟著馬的即時速度變化。沒有動畫的模型只做上下起伏。
    /// </summary>
    public sealed class HorseBodyModel : IHorseBody
    {
        private const string PreviewClipPrefix = "__preview__";

        /// <summary>速度比低於這個值時動畫接近靜止（閘門待機時的比值約 0.12）。</summary>
        private const float StandStillRatio = 0.5f;

        /// <summary>跑步動畫速度倍率的對應範圍：速度比 0.4 → 最慢、1.5 → 最快（正常奔跑約 1.0）。</summary>
        private const float SlowRatio = 0.4f;
        private const float FastRatio = 1.5f;

        /// <summary>有待機動畫時，速度比超過待機值多少就完全切成跑步動畫。</summary>
        private const float IdleBlendRange = 0.3f;
        private const float IdleRatio = 0.12f;

        private const float LabelMargin = 0.4f;
        private const float BobHeight = 0.06f;

        private readonly Transform _model;
        private readonly Vector3 _restPosition;
        private readonly HorseModelConfig _config;
        private readonly float _height;
        private readonly float _length;

        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationClipPlayable _runPlayable;
        private AnimationClipPlayable _idlePlayable;
        private AnimationClip _runClip;
        private AnimationClip _idleClip;
        private float _runTime;
        private float _idleTime;
        private float _bobTime;

        private HorseBodyModel(Transform model, HorseModelConfig config, float height, float length)
        {
            _model = model;
            _restPosition = model.localPosition;
            _config = config;
            _height = height;
            _length = length;
        }

        public float LabelHeight
        {
            get { return _height + LabelMargin; }
        }

        public float Length
        {
            get { return _length; }
        }

        /// <summary>載入並擺好模型；找不到或出錯時回傳 null，呼叫端退回方塊馬。</summary>
        public static HorseBodyModel TryCreate(Transform parent, string resourcePath, HorseModelConfig config, Color coat)
        {
            try
            {
                GameObject prefab = Resources.Load<GameObject>(resourcePath);
                if (prefab == null)
                {
                    Debug.LogWarning("[HorseBodyModel] 找不到馬匹模型 Resources/" + resourcePath + "，改用方塊馬。");
                    return null;
                }

                GameObject instance = Object.Instantiate(prefab, parent);
                instance.name = "Model_" + prefab.name;
                Transform model = instance.transform;
                Bounds bounds = PlaceModel(parent, model, prefab.transform, config);
                TintMaterials(instance, config.TintMaterialKeyword, coat);

                HorseBodyModel body = new HorseBodyModel(model, config, bounds.size.y, bounds.size.x);
                body.SetupAnimation(instance, resourcePath);
                return body;
            }
            catch (Exception error)
            {
                Debug.LogError("[HorseBodyModel] 建立馬匹模型 " + resourcePath + " 失敗，改用方塊馬。原因："
                               + error.GetType().Name + " - " + error.Message);
                return null;
            }
        }

        public void Animate(float speedRatio, float deltaTime)
        {
            if (!_graph.IsValid())
            {
                // 沒有動畫的模型：做一點上下起伏，至少看得出在跑
                float ratio = Mathf.Clamp01(speedRatio);
                _bobTime += deltaTime * Mathf.Lerp(1.6f, 4.2f, ratio);
                _model.localPosition = _restPosition
                                       + Vector3.up * (Mathf.Sin(_bobTime * Mathf.PI * 2f) * BobHeight * ratio);
                return;
            }

            float runSpeed = RunPlaybackSpeed(speedRatio);
            _runTime += deltaTime * runSpeed;
            _runPlayable.SetTime(Loop(_runTime, _runClip));

            float idleWeight = 0f;
            if (_idleClip != null)
            {
                _idleTime += deltaTime;
                _idlePlayable.SetTime(Loop(_idleTime, _idleClip));
                idleWeight = 1f - Mathf.Clamp01((speedRatio - IdleRatio) / IdleBlendRange);
            }

            _mixer.SetInputWeight(0, 1f - idleWeight);
            if (_idleClip != null)
            {
                _mixer.SetInputWeight(1, idleWeight);
            }

            _graph.Evaluate(0f);
        }

        public void ResetPose()
        {
            _runTime = 0f;
            _idleTime = 0f;
            _bobTime = 0f;
            _model.localPosition = _restPosition;
        }

        public void Dispose()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }
        }

        /// <summary>依速度比算跑步動畫的播放倍率；有待機動畫時站著不需要放慢，沒有的話站著幾乎靜止。</summary>
        private float RunPlaybackSpeed(float speedRatio)
        {
            float t = Mathf.Clamp01((speedRatio - SlowRatio) / (FastRatio - SlowRatio));
            float speed = Mathf.Lerp((float)_config.MinAnimationSpeed, (float)_config.MaxAnimationSpeed, t);
            if (_idleClip == null)
            {
                speed *= Mathf.Clamp01(speedRatio / StandStillRatio);
            }

            return speed;
        }

        private static double Loop(float time, AnimationClip clip)
        {
            return clip.length > 0.0001f ? time % clip.length : 0.0;
        }

        // ---- 擺放與外觀 ----

        /// <summary>轉向、縮放、腳底貼地並置中，回傳相對馬匹原點的包圍盒（只用尺寸）。</summary>
        private static Bounds PlaceModel(Transform parent, Transform model, Transform prefab, HorseModelConfig config)
        {
            model.localPosition = Vector3.zero;
            model.localRotation = Quaternion.Euler(0f, (float)config.YawDegrees, 0f) * prefab.localRotation;
            model.localScale = prefab.localScale * (float)config.Scale;

            Bounds bounds = MeasureRelative(parent, model.gameObject);
            if (config.FitLength > 0.0 && bounds.size.x > 0.0001f)
            {
                model.localScale = prefab.localScale * ((float)config.FitLength / bounds.size.x);
                bounds = MeasureRelative(parent, model.gameObject);
            }

            model.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            return MeasureRelative(parent, model.gameObject);
        }

        private static Bounds MeasureRelative(Transform parent, GameObject instance)
        {
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            bounds.center -= parent.position;
            return bounds;
        }

        private static void TintMaterials(GameObject instance, string keyword, Color coat)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return;
            }

            string needle = keyword.ToLowerInvariant();
            Dictionary<Material, Material> tinted = new Dictionary<Material, Material>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material original = materials[i];
                    if (original == null || !original.name.ToLowerInvariant().Contains(needle))
                    {
                        continue;
                    }

                    Material copy;
                    if (!tinted.TryGetValue(original, out copy))
                    {
                        copy = new Material(original) { name = original.name + "_Tint", color = coat };
                        tinted[original] = copy;
                    }

                    materials[i] = copy;
                }

                renderer.sharedMaterials = materials;
            }
        }

        // ---- 動畫 ----

        private void SetupAnimation(GameObject instance, string resourcePath)
        {
            AnimationClip[] clips = LoadClips(resourcePath);
            _idleClip = PickClip(clips, _config.IdleClip, false, null);
            _runClip = PickClip(clips, _config.RunClip, true, _idleClip);
            if (_runClip == null)
            {
                Debug.Log("[HorseBodyModel] " + resourcePath + " 沒有可用的動畫片段，只做上下起伏。");
                return;
            }

            Animator animator = instance.GetComponentInChildren<Animator>();
            if (animator == null)
            {
                animator = instance.AddComponent<Animator>();
            }

            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // 手動推進時間：每幀自己設定片段時間再求值，循環與變速都由程式掌控
            _graph = PlayableGraph.Create("Horse_" + instance.name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Animation", animator);

            _mixer = AnimationMixerPlayable.Create(_graph, _idleClip != null ? 2 : 1);
            _runPlayable = AnimationClipPlayable.Create(_graph, _runClip);
            _graph.Connect(_runPlayable, 0, _mixer, 0);
            if (_idleClip != null)
            {
                _idlePlayable = AnimationClipPlayable.Create(_graph, _idleClip);
                _graph.Connect(_idlePlayable, 0, _mixer, 1);
            }

            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        private static AnimationClip[] LoadClips(string resourcePath)
        {
            List<AnimationClip> usable = new List<AnimationClip>();
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(resourcePath))
            {
                if (clip != null && !clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal))
                {
                    usable.Add(clip);
                }
            }

            return usable.ToArray();
        }

        /// <summary>
        /// 依名稱挑片段（不分大小寫）；名稱空白時，<paramref name="fallbackToFirst"/> 決定要不要拿第一個
        /// 不是 <paramref name="exclude"/> 的片段（避免自動挑到待機動畫當跑步動畫）。
        /// </summary>
        private static AnimationClip PickClip(AnimationClip[] clips, string wanted, bool fallbackToFirst,
                                              AnimationClip exclude)
        {
            if (!string.IsNullOrEmpty(wanted))
            {
                foreach (AnimationClip clip in clips)
                {
                    if (string.Equals(clip.name, wanted, StringComparison.OrdinalIgnoreCase))
                    {
                        return clip;
                    }
                }

                Debug.LogWarning("[HorseBodyModel] 找不到動畫片段 \"" + wanted + "\"。");
            }

            if (!fallbackToFirst)
            {
                return null;
            }

            foreach (AnimationClip clip in clips)
            {
                if (clip != exclude)
                {
                    return clip;
                }
            }

            return null;
        }
    }
}
