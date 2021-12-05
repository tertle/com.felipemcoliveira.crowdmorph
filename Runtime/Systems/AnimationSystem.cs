using Unity.Entities;
using UnityEngine;
using Unity.Mathematics;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;

namespace CrowdMorph
{
   public struct EntityClipEvent
   {
      public Entity Entity;
      public int IntParameter;
      public float FloatParameter;
   }

   public abstract class AnimationSystemBase : SystemBase
   {
      int m_BoneCountPropertyID;
      int m_CommandBufferIndexPropertyID;
      int m_CommandCountPropertyID;

      internal ClipBufferManager ClipBufferManager { get; private set; }
      internal AnimationCommandBufferManager AnimationCommandBufferManager { get; private set; }
      internal SkeletonBufferManager SkeletonBufferManager { get; private set; }

      ComputeShader m_AnimationComputeShader;
      int m_AnimationComputeKernel;
      uint m_AnimationThreadGroupSize;
      List<SharedSkeleton> m_AllSharedSkeleton;
      NativeMultiHashMap<StringHash, EntityClipEvent> m_EntityClipEvents;

      JobHandle m_CommandProducerHandle;

      protected override void OnCreate()
      {
         m_EntityClipEvents = new NativeMultiHashMap<StringHash, EntityClipEvent>(16 * 1024, Allocator.Persistent);

         m_AnimationComputeShader = Resources.Load<ComputeShader>("CrowdMorph/AnimationComputeShader");
         m_AnimationComputeKernel = m_AnimationComputeShader.FindKernel("AnimationComputeKernel");
          
         m_AnimationComputeShader.GetKernelThreadGroupSizes(m_AnimationComputeKernel, out m_AnimationThreadGroupSize, out var _, out var _);

         m_AllSharedSkeleton = new List<SharedSkeleton>();

         ClipBufferManager = new ClipBufferManager();
         ClipBufferManager.OnCreate();

         AnimationCommandBufferManager = new AnimationCommandBufferManager();
         AnimationCommandBufferManager.OnCreate(); 

         SkeletonBufferManager = new SkeletonBufferManager();
         SkeletonBufferManager.OnCreate();

         m_BoneCountPropertyID = Shader.PropertyToID("g_BoneCount");
         m_CommandBufferIndexPropertyID = Shader.PropertyToID("g_CommandBufferIndex");
         m_CommandCountPropertyID = Shader.PropertyToID("g_CommandCount");
      }

      protected override void OnDestroy()
      {
         ClipBufferManager.OnDestroy();
         AnimationCommandBufferManager.OnDestroy();
         SkeletonBufferManager.OnDestroy();

         m_EntityClipEvents.Dispose();
      }

      protected unsafe override void OnUpdate()
      {
         m_CommandProducerHandle.Complete();
         m_CommandProducerHandle = new JobHandle();
         
         m_AllSharedSkeleton.Clear();
         EntityManager.GetAllUniqueSharedComponentData(m_AllSharedSkeleton);

         var skeletons = new NativeList<BlobAssetReference<SkeletonDefinition>>(8, Allocator.Temp);

         foreach (var sharedSkeleton in m_AllSharedSkeleton)
            skeletons.Add(sharedSkeleton.Value);

         var commandBatches = new NativeList<AnimationCommandBatch>(8, Allocator.Temp);
         AnimationCommandBufferManager.PushAnimationCommandsToBuffer(skeletons, commandBatches);

         ClipBufferManager.PushAnimationMatricesBufferToShader(m_AnimationComputeShader, m_AnimationComputeKernel);
         AnimationCommandBufferManager.PushAnimationCommandsBufferToShader(m_AnimationComputeShader, m_AnimationComputeKernel);
         SkeletonBufferManager.PushSkeletonMatricesToShader(m_AnimationComputeShader, m_AnimationComputeKernel);
         SkeletonBufferManager.PushSkeletonMasksToShader(m_AnimationComputeShader, m_AnimationComputeKernel);

         foreach (var commandBatch in commandBatches)
         {
            m_AnimationComputeShader.SetInt(m_BoneCountPropertyID, commandBatch.Skeleton.Value.BoneCount);
            m_AnimationComputeShader.SetInt(m_CommandBufferIndexPropertyID, commandBatch.ComputeBufferStartIndex);
            m_AnimationComputeShader.SetInt(m_CommandCountPropertyID, commandBatch.CommandCount);
            
            int threadGroupsX = (int)math.ceil((float)commandBatch.CommandCount / m_AnimationThreadGroupSize);
            m_AnimationComputeShader.Dispatch(m_AnimationComputeKernel, threadGroupsX, 1, 1);
         }

         skeletons.Dispose();
         commandBatches.Dispose(); 

      }

      public void AddJobHandleForAnimationCommandProducer(JobHandle handle)
      {
         m_CommandProducerHandle = JobHandle.CombineDependencies(handle, m_CommandProducerHandle);
      }

      public void ClearClipEvents()
      {
         m_EntityClipEvents.Clear();
      }

      public NativeMultiHashMap<StringHash, EntityClipEvent> GetClipEvents()
      {
         return m_EntityClipEvents;
      }

      public AnimationContext GetContext()
      {
         return new AnimationContext
         {
            ClipInstanceHashToSampleBufferIndex = ClipBufferManager.ClipInstanceHashToSampleBufferIndex,
            SkeletonHashToAnimationCommandList = AnimationCommandBufferManager.SkeletonHashToAnimationCommandList,
            SkeletonMaskInstanceHashToBufferIndex = SkeletonBufferManager.SkeletonMaskInstanceHashToBufferIndex,
            EntityClipEvents = m_EntityClipEvents.AsParallelWriter(),
            ClipInstanceHashToClip = ClipBufferManager.ClipInstanceHashToClip
         }; 
      }
   }
}