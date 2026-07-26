namespace FaceSlapper.Battle
{
    /// <summary>
    /// 力量 Buff：持续时间内提升角色的技能威力（如击飞力度）。
    /// 用法：GetComponent&lt;BuffComponent&gt;().AddBuff(new PowerBuff(10f, 1.5f));
    /// </summary>
    public class PowerBuff : BuffBase
    {
        private readonly float _duration;
        private readonly float _multiplier;

        public PowerBuff(float duration = 10f, float multiplier = 1.5f)
        {
            _duration = duration;
            _multiplier = multiplier;
            buffName = "PowerBuff";
        }

        public override float Duration => _duration;

        public override float PowerMultiplier => _multiplier;
    }
}
