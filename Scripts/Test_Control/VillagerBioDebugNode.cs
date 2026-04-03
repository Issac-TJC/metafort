using Godot;
using System;
using MetaFort.Core.ECS;

namespace MetaFort.Test_Control
{
    [GlobalClass]
    public partial class VillagerBioDebugNode : Node
    {
        [ExportCategory("ECS Connection")]
        [Export] public bool TrackSelectedVillager { get; set; } = true;
        [Export] public uint TargetEntityId { get; set; } = 0;

        [ExportCategory("Control")]
        [Export] public bool OverrideECS { get; set; } = false;

        [ExportCategory("Biological Stats")]
        [Export] public Gender Gender;
        [Export(PropertyHint.Range, "0,100")] public float Libido;
        [Export(PropertyHint.Range, "0,100")] public float Hunger;
        [Export(PropertyHint.Range, "0,100")] public float Stamina;
        [Export(PropertyHint.Range, "0,100")] public float Sanity;

        private IEntityManager _entityManager;

        public override void _Ready()
        {
            if (GameEntry.Instance != null)
            {
                _entityManager = GameEntry.Instance.EntityManager;
            }
        }

        public override void _Process(double delta)
        {
            if (_entityManager == null) return;

            uint activeEntity = TargetEntityId;

            if (TrackSelectedVillager)
            {
                ReadOnlySpan<uint> selectedIds = _entityManager.GetDenseEntityIds<PlayerSelectedComponent>();
                if (selectedIds.Length > 0)
                {
                    activeEntity = selectedIds[0];
                }
                else
                {
                    TargetEntityId = 0;
                    return; // Nobody selected
                }
            }
            
            TargetEntityId = activeEntity;

            if (_entityManager.IsAlive(activeEntity) && _entityManager.HasComponent<BiologicalComponent>(activeEntity))
            {
                ref var bio = ref _entityManager.GetComponent<BiologicalComponent>(activeEntity);

                if (OverrideECS)
                {
                    // WRITE to ECS
                    bio.Gender = Gender;
                    bio.Libido = Libido;
                    bio.Hunger = Hunger;
                    bio.Stamina = Stamina;
                    bio.Sanity = Sanity;
                }
                else
                {
                    // READ from ECS
                    Gender = bio.Gender;
                    Libido = bio.Libido;
                    Hunger = bio.Hunger;
                    Stamina = bio.Stamina;
                    Sanity = bio.Sanity;
                }
            }
        }
    }
}
