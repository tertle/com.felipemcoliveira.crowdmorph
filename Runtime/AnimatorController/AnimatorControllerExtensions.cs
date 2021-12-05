using Unity.Entities;

namespace CrowdMorph
{
   public static class AnimatorControllerExtensions
   {
      public static void Reset(this DynamicBuffer<LayerState> layerStates)
      {
         for (int i = 0; i < layerStates.Length; i++)
            layerStates[i] = default;
      }
   }
}