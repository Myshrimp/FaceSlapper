using System.Collections.Generic;

namespace FaceSlapper
{
    public class Effect
    {
        public string Name { get; set; }
        public bool Predictable;
        public int TargetPlayer;
        public virtual bool ShouldRollback() {  return false; }
        public virtual void Rollback() { }
        public virtual void Predict() { }
        public virtual void Apply() { }
    }

    public enum EffectTag
    {
        Locomotion, Control, Buff
    }
    public class ForgeSystem
    {
        public List<int> Players;

    }
}
