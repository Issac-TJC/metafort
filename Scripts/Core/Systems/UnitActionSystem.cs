using System.Collections.Generic;
using MetaFort.Core.ECS;
using MetaFort.Core.EventBus;

namespace MetaFort.Core.Systems
{
    /// <summary>
    /// 单位业务行为指令执行与机甲状态拦截系统
    /// </summary>
    public class UnitActionSystem : ISystem
    {
        private IEntityManager _entityManager;
        private IEventBus _eventBus;
        
        // 修正 2：指令队列化 (Command Queuing)
        // 所有的业务指令在回调中先入队，在 Update 周期统一弹出处理，保证状态修改受控
        private readonly Queue<UnitCommandEvent> _commandQueue = new Queue<UnitCommandEvent>();

        public void Initialize(IEntityManager entityManager, IEventBus eventBus)
        {
            _entityManager = entityManager;
            _eventBus = eventBus;
            
            // 订阅核心指令事件
            _eventBus.Subscribe<UnitCommandEvent>(OnUnitCommand);
        }

        private void OnUnitCommand(ref UnitCommandEvent evt)
        {
            // 回调函数只负责把事件 Enqueue，严禁在这里直接执行具有副作用的业务逻辑
            _commandQueue.Enqueue(evt);
        }

        public void Update(double deltaTime)
        {
            // 在系统的 Update 生命周期中安全、顺序地处理积压的指令队列
            while (_commandQueue.TryDequeue(out var cmd))
            {
                ProcessCommand(cmd);
            }
        }

        private void ProcessCommand(UnitCommandEvent cmd)
        {
            uint entityId = cmd.TargetUnit;
            
            // 如果实体在此时已经死亡或被销毁，丢弃指令
            if (!_entityManager.IsAlive(entityId)) return;

            // 核心要求：机甲上下车逻辑拦截 (通过组件进行完全解耦的组合多态)
            bool hasMech = _entityManager.HasComponent<MechComponent>(entityId);
            bool isMounted = false;

            if (hasMech)
            {
                isMounted = _entityManager.GetComponent<MechComponent>(entityId).IsMounted;
            }

            // 1. 机甲乘员的生理行为限制：必须下机甲才能进行
            if (IsBiologicalAction(cmd.Type) && isMounted)
            {
                // [强制触发 DismountMech 逻辑] 代表驾驶员从机甲下来
                ref var mech = ref _entityManager.GetComponent<MechComponent>(entityId);
                mech.IsMounted = false;
                
                // 【中文注释】：这里体现了 ECS 架构下的多态特性。我们借由判断 MechComponent 的存在与否，
                // 在不知道目标到底是什么实体的情况下，无缝拦截并中断了基础的生物行为逻辑，
                // 将单纯的“生理需求执行”转变成了复杂的“机甲脱出 -> （生理逻辑由外部或二次指令重发）”的业务流。
                
                // 本次生物指令被强制拦截为下车动作，停止执行后续的生物逻辑
                return; 
            }

            // 2. 根据指令执行相应业务操作
            switch (cmd.Type)
            {
                case CommandType.FireCannon:
                    // 必须校验是否拥有机甲、是否处于搭载状态，且弹药大于0
                    if (hasMech && isMounted)
                    {
                        ref var mech = ref _entityManager.GetComponent<MechComponent>(entityId);
                        if (mech.AmmoCount > 0)
                        {
                            mech.AmmoCount--;
                            // TODO: TODO：实际开火的副作用逻辑（如生成投掷物实体、播放特效等）
                        }
                    }
                    break;

                case CommandType.Eat:
                    if (_entityManager.HasComponent<BiologicalComponent>(entityId))
                    {
                        ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(entityId);
                        bio.Hunger -= 20f; // 吃饭减少饥饿值
                        if (bio.Hunger < 0) bio.Hunger = 0;
                    }
                    break;

                case CommandType.Sleep:
                    if (_entityManager.HasComponent<BiologicalComponent>(entityId))
                    {
                        ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(entityId);
                        bio.Stamina += 40f; // 睡觉恢复体力
                        if (bio.Stamina > 100f) bio.Stamina = 100f;
                    }
                    break;
                    
                // 其他逻辑可在此拓展...
            }
        }

        private bool IsBiologicalAction(CommandType type)
        {
            return type == CommandType.Eat || 
                   type == CommandType.Sleep || 
                   type == CommandType.Mate;
        }
    }
}
