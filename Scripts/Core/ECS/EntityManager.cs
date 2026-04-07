using System;
using System.Collections.Generic;

namespace MetaFort.Core.ECS
{
    // 非泛型基底接口，仅仅为了能用 Dictionary 进行集中式管理
    public interface IComponentArray
    {
        void RemoveEntity(uint entityId);
    }

    /// <summary>
    /// 基于 Struct of Arrays (SOA) 与稀疏集紧凑数组 (Sparse Set) 
    /// 现已补全 Join 查询机制，底部 Dense 数组完整保留了包含 Generation 的 EntityId。
    /// </summary>
    public class ComponentArray<T> : IComponentArray where T : struct, IComponent
    {
        private readonly T[] _components;
        private readonly int[] _entityToIndex;
        
        // 核心改动：不再只存低位Index，必须存满 32bit 的 EntityId
        private readonly uint[] _indexToEntity; 
        private int _size;

        public ComponentArray(int maxEntities)
        {
            _components = new T[maxEntities];
            _entityToIndex = new int[maxEntities];
            _indexToEntity = new uint[maxEntities];
            Array.Fill(_entityToIndex, -1);
            _size = 0;
        }

        public void Add(uint entityId, in T component)
        {
            int entityIndex = (int)(entityId & 0x00FFFFFF);
            if (_entityToIndex[entityIndex] != -1) return; // 已经存在
            
            int targetIndex = _size;
            _components[targetIndex] = component; 
            _entityToIndex[entityIndex] = targetIndex;
            
            // 将完整的(带世代)的ID塞进 Dense 数组，支撑后续完美精准的 0 GC 联合遍历查询
            _indexToEntity[targetIndex] = entityId; 
            _size++;
        }

        public ref T Get(int entityIndex)
        {
            int targetIndex = _entityToIndex[entityIndex];
            if (targetIndex == -1) throw new Exception($"Entity {entityIndex} does not have component {typeof(T).Name}");
            return ref _components[targetIndex];
        }

        public bool Has(int entityIndex)
        {
            return _entityToIndex[entityIndex] != -1;
        }

        public void Remove(uint entityId)
        {
            int entityIndex = (int)(entityId & 0x00FFFFFF);
            int targetIndex = _entityToIndex[entityIndex];
            if (targetIndex == -1) return;

            int lastIndex = _size - 1;
            uint lastEntityId = _indexToEntity[lastIndex];

            // 保持致密：尾部平移填位
            if (targetIndex != lastIndex)
            {
                _components[targetIndex] = _components[lastIndex];
                
                int lastEntityIndex = (int)(lastEntityId & 0x00FFFFFF);
                _entityToIndex[lastEntityIndex] = targetIndex;
                _indexToEntity[targetIndex] = lastEntityId;
            }

            _entityToIndex[entityIndex] = -1;
            _size--;
        }

        public void RemoveEntity(uint entityId)
        {
            Remove(entityId);
        }

        // ===================================
        // 新增的 Join 查询特权 API
        // ===================================
        
        public int CurrentCount => _size;

        public ReadOnlySpan<uint> GetDenseEntityIds() => new ReadOnlySpan<uint>(_indexToEntity, 0, _size);
    }

    /// <summary>
    /// 包含世代校验位域的 EntityManager
    /// </summary>
    public class EntityManager : IEntityManager
    {
        private const int MAX_ENTITIES = 10000;
        private readonly byte[] _generations; 
        private readonly Queue<int> _availableIndices;

        private readonly Dictionary<Type, IComponentArray> _componentArrays;

        public EntityManager(int maxCapacity = MAX_ENTITIES)
        {
            _generations = new byte[maxCapacity];
            _availableIndices = new Queue<int>(maxCapacity);
            _componentArrays = new Dictionary<Type, IComponentArray>();

            for (int i = 0; i < maxCapacity; i++)
            {
                _availableIndices.Enqueue(i);
                _generations[i] = 0;
            }
        }

        public uint CreateEntity()
        {
            if (_availableIndices.Count == 0) throw new Exception("Max capacities for entities reached!");

            int index = _availableIndices.Dequeue();
            byte generation = _generations[index];

            return ((uint)generation << 24) | ((uint)index & 0x00FFFFFF);
        }

        public void DestroyEntity(uint entityId)
        {
            if (!IsAlive(entityId)) return;

            int index = (int)(entityId & 0x00FFFFFF);

            foreach (IComponentArray componentArray in _componentArrays.Values)
            {
                componentArray.RemoveEntity(entityId);
            }

            _generations[index]++; 
            _availableIndices.Enqueue(index);
        }

        public bool IsAlive(uint entityId)
        {
            int index = (int)(entityId & 0x00FFFFFF);
            byte generation = (byte)(entityId >> 24);
            
            return index < _generations.Length && _generations[index] == generation; 
        }

        private ComponentArray<T> GetComponentArray<T>() where T : struct, IComponent
        {
            Type t = typeof(T);
            if (!_componentArrays.TryGetValue(t, out IComponentArray arrayObj))
            {
                arrayObj = new ComponentArray<T>(_generations.Length);
                _componentArrays[t] = arrayObj;
            }
            return (ComponentArray<T>)arrayObj;
        }

        public void AddComponent<T>(uint entityId, in T component) where T : struct, IComponent
        {
            if (!IsAlive(entityId)) return;
            GetComponentArray<T>().Add(entityId, component); // 修改为传入完整 entityId
        }

        public ref T GetComponent<T>(uint entityId) where T : struct, IComponent
        {
            int index = (int)(entityId & 0x00FFFFFF);
            return ref GetComponentArray<T>().Get(index);
        }

        public bool HasComponent<T>(uint entityId) where T : struct, IComponent
        {
            if (!IsAlive(entityId)) return false;
            int index = (int)(entityId & 0x00FFFFFF);
            return GetComponentArray<T>().Has(index);
        }

        public void RemoveComponent<T>(uint entityId) where T : struct, IComponent
        {
            if (!IsAlive(entityId)) return;
            GetComponentArray<T>().Remove(entityId); // 修改为传入完整 entityId
        }

        // ===================================
        // 桥接到 ComponentArray 的查询外衣
        // ===================================

        public int GetComponentCount<T>() where T : struct, IComponent
        {
            return GetComponentArray<T>().CurrentCount;
        }

        public ReadOnlySpan<uint> GetDenseEntityIds<T>() where T : struct, IComponent
        {
            return GetComponentArray<T>().GetDenseEntityIds();
        }
    }
}
