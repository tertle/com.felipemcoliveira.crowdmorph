#if UNITY_EDITOR

using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEditor;

namespace CrowdMorph.Hybrid
{
   public static class ClipBuilder
   {
      public static BlobAssetReference<Clip> Build(AnimationClip authoringClip, List<string> filterBinding = null)
      {
         if (authoringClip == null)
            return BlobAssetReference<Clip>.Null;
         
         Assert.IsFalse(authoringClip.isHumanMotion, "Human montion is not supported.");

         var animationCurveBindings = AnimationUtility.GetCurveBindings(authoringClip);

         var translationBindings = new List<EditorCurveBinding>(32);
         var rotationBindings = new List<EditorCurveBinding>(32);
         var scaleBindings = new List<EditorCurveBinding>(32); 

         foreach (var curveBinding in animationCurveBindings)
         {
            if (filterBinding != null && !filterBinding.Contains(curveBinding.path))
               continue;

            if (curveBinding.type == typeof(Transform))
            {
               switch (curveBinding.propertyName)
               {
                  case "m_LocalPosition.x":
                     translationBindings.Add(curveBinding);
                     break;
                  case "m_LocalRotation.x":
                     rotationBindings.Add(curveBinding);
                     break;
                  case "m_LocalScale.x":
                     scaleBindings.Add(curveBinding);
                     break;
               }
            }
         }

         Core.ValidateAreEqual(translationBindings.Count, rotationBindings.Count);
         Core.ValidateAreEqual(translationBindings.Count, scaleBindings.Count);

         int boneCount = translationBindings.Count;
         var blobBuilder = new BlobBuilder(Allocator.Temp);
         
         ref var clip = ref blobBuilder.ConstructRoot<Clip>();
         clip.FrameRate = authoringClip.frameRate;
         clip.Length = authoringClip.length;
         clip.WrapMode = authoringClip.isLooping ? WrapMode.Loop : WrapMode.Once;

         var rotations = blobBuilder.Allocate(ref clip.LocalRotations, rotationBindings.Count * clip.SampleCount);
         var translations = blobBuilder.Allocate(ref clip.LocalTranslations, translationBindings.Count * clip.SampleCount);
         var scales = blobBuilder.Allocate(ref clip.LocalScales, scaleBindings.Count * clip.SampleCount);
         var bindings = blobBuilder.Allocate(ref clip.Bindings, translationBindings.Count);

         var scratchCurvers = new AnimationCurve[4];

         for (int i = 0; i < boneCount; i++)
         {
            bindings[i] = translationBindings[i].path;

            scratchCurvers[0] = GetAnimationTransformCurve("m_LocalRotation.x", authoringClip, translationBindings[i]);
            scratchCurvers[1] = GetAnimationTransformCurve("m_LocalRotation.y", authoringClip, translationBindings[i]);
            scratchCurvers[2] = GetAnimationTransformCurve("m_LocalRotation.z", authoringClip, translationBindings[i]);
            scratchCurvers[3] = GetAnimationTransformCurve("m_LocalRotation.w", authoringClip, translationBindings[i]);
            ConvertCurves(ref clip, ref rotations, scratchCurvers, i, boneCount);

            scratchCurvers[0] = GetAnimationTransformCurve("m_LocalPosition.x", authoringClip, rotationBindings[i]);
            scratchCurvers[1] = GetAnimationTransformCurve("m_LocalPosition.y", authoringClip, rotationBindings[i]);
            scratchCurvers[2] = GetAnimationTransformCurve("m_LocalPosition.z", authoringClip, rotationBindings[i]);
            ConvertCurves(ref clip, ref translations, scratchCurvers, i, boneCount);

            scratchCurvers[0] = GetAnimationTransformCurve("m_LocalScale.x", authoringClip, scaleBindings[i]);
            scratchCurvers[1] = GetAnimationTransformCurve("m_LocalScale.y", authoringClip, scaleBindings[i]);
            scratchCurvers[2] = GetAnimationTransformCurve("m_LocalScale.z", authoringClip, scaleBindings[i]);
            ConvertCurves(ref clip, ref scales, scratchCurvers, i, boneCount);
         }

         var events = blobBuilder.Allocate(ref clip.Events, authoringClip.events.Length);

         for (int i = 0; i < events.Length; i++)
         {
            var authoringEvent = authoringClip.events[i];
            events[i].FunctionNameHash = authoringEvent.functionName;
            events[i].IntParameter = authoringEvent.intParameter;
            events[i].FloatParameter = authoringEvent.floatParameter;
            events[i].Time = authoringEvent.time;
         }

         var outputClip = blobBuilder.CreateBlobAssetReference<Clip>(Allocator.Persistent);
         outputClip.Value.HashCode = HashUtility.ComputeClipHash(ref outputClip.Value);
         blobBuilder.Dispose();

         return outputClip;
      }

      private static AnimationCurve GetAnimationTransformCurve(string property, AnimationClip clip, EditorCurveBinding curveBinding)
      {
         curveBinding.propertyName = property;
         return AnimationUtility.GetEditorCurve(clip, curveBinding);
      }

      private static void ConvertCurves<T>(ref Clip clip, ref BlobBuilderArray<T> dest, AnimationCurve[] curves, int boneIndex, int boneCount) where T : unmanaged
      {
         var lastValue = default(T);
         for (var frame = 0; frame < clip.FrameCount; frame++)
         {
            lastValue = Evaluate<T>(curves, frame / clip.FrameRate);
            dest[frame * boneCount + boneIndex] = lastValue;
         }
         var atDurationVale = Evaluate<T>(curves, clip.Length);
         dest[clip.FrameCount * boneCount + boneIndex] = AdjustLastFrameValue(lastValue, atDurationVale, clip.LastFrameError);
      }

      private unsafe static T AdjustLastFrameValue<T>(T beforeLastFrame, T atDurationValue, float lastFrameError) where T : unmanaged
      {
         Assert.IsTrue(sizeof(T) % sizeof(float) == 0);
         T result = default;
         for (int valueElementIdx = 0; valueElementIdx < sizeof(T) / sizeof(float); valueElementIdx++)
         {
            float beforeLastFrameAsFloat = UnsafeUtility.ReadArrayElement<float>(&beforeLastFrame, valueElementIdx);
            float atDurationValueAsFloat = UnsafeUtility.ReadArrayElement<float>(&atDurationValue, valueElementIdx);
            UnsafeUtility.WriteArrayElement(&result, valueElementIdx, AdjustLastFrameValue(beforeLastFrameAsFloat, atDurationValueAsFloat, lastFrameError));
         }
         return result;
      }

      private static float AdjustLastFrameValue(float beforeLastValue, float atDurationValue, float lastFrameError)
      {
         return lastFrameError < 1.0f ? math.lerp(beforeLastValue, atDurationValue, 1.0f / (1.0f - lastFrameError)) : atDurationValue;
      }

      private unsafe static T Evaluate<T>(AnimationCurve[] curves, float t) where T : unmanaged
      {
         int floatCount = sizeof(T) / sizeof(float);

         Assert.IsTrue(sizeof(T) % sizeof(float) == 0);
         Assert.IsTrue(floatCount <= curves.Length);

         var result = default(T);

         for (int elementIdx = 0; elementIdx < floatCount; elementIdx++)
         {
            float value = curves[elementIdx].Evaluate(t);
            UnsafeUtility.WriteArrayElement(&result, elementIdx, value);
         }

         return result;
      }
   }
}

#endif