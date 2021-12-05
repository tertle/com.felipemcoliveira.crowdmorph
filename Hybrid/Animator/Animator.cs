using System.Collections.Generic;
using UnityEngine;

namespace CrowdMorph.Hybrid
{
   [AddComponentMenu("CrowdMorph/Animator")]
   public class Animator : MonoBehaviour, IAnimationClipSource
   {
      [SerializeField]
      RuntimeAnimatorController m_Controller;

      [SerializeField]
      SerializableComponentType m_ParametersComponentType;

      [SerializeField]
      List<AnimatorOverrideController> m_AnimatorOverrideControllers;
      
      public RuntimeAnimatorController Controller
      {
         get => m_Controller;
         set => m_Controller = value;
      }

      public SerializableComponentType ParametersComponentType
      {
         get => m_ParametersComponentType;
         set => m_ParametersComponentType = value;
      }

      public List<AnimatorOverrideController> AnimatorOverrideControllers
      {
         get => m_AnimatorOverrideControllers;
         set => m_AnimatorOverrideControllers = value;
      }

      public void GetAnimationClips(List<AnimationClip> results)
      {
#if UNITY_EDITOR
         var clips = AnimatorControllerUtility.GetAnimationClips(m_Controller);
         if (clips != null)
            results.AddRange(clips);
#endif
      }
   }
}  