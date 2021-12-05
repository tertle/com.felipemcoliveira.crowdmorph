using System;
using UnityEngine;

namespace CrowdMorph
{
   [Serializable]
   public unsafe struct SerializableComponentType : ISerializationCallbackReceiver
   {
      [SerializeField]
      string m_TypeName;

      Type m_Type;

      public Type Type
      {
         get => m_Type;
         set
         {
            m_Type = value;
            m_TypeName = value == null ? string.Empty : value.AssemblyQualifiedName;
         }
      }


      public void OnAfterDeserialize()
      {
         if (!string.IsNullOrWhiteSpace(m_TypeName))
         {
            try
            {
               m_Type = Type.GetType(m_TypeName, true);
            }
            catch (Exception)
            {
               Debug.LogErrorFormat("[{0}] Unable to find type {1}", nameof(SerializableComponentType), m_TypeName);
            }

         }
         else
         {
            m_Type = null;
            m_TypeName = "";
         }
      }

      public void OnBeforeSerialize()
      {
         m_TypeName = m_Type != null ? m_Type.AssemblyQualifiedName : string.Empty;
      }

      public static implicit operator SerializableComponentType(Type type)
      {
         return new SerializableComponentType
         {
            m_Type = type,
            m_TypeName = type == null ? "" : type.AssemblyQualifiedName
         };
      }

      public static implicit operator Type(SerializableComponentType SerializableComponentType)
      {
         return SerializableComponentType.Type;
      }
   }
}