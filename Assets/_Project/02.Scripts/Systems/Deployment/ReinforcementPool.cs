using System.Collections.Generic;
using SRPG.Data;
using UnityEngine;

namespace SRPG.Systems.Deployment
{
    /// <summary>
    /// 전투 서열을 들고 있다가, 전장에 <b>자리가 나면</b> 다음 분대를 내보냅니다.
    ///
    /// <b>왜 시간이 아니라 자리인가</b>
    ///
    /// 예전에는 파도가 시각에 맞춰 밀려왔습니다. 배가 오는 간격이 곧 압박이었고,
    /// 방어자는 그 시계에 맞춰 전선을 옮겼습니다.
    ///
    /// 야전은 그렇지 않습니다. 양쪽 다 전 병력을 데리고 왔지만 전장이 좁아
    /// 한 번에 다 붙을 수 없을 뿐입니다. 앞에 선 부대가 무너지면 뒤가 올라옵니다.
    /// 그래서 투입 조건은 시각이 아니라 <b>빈자리</b>입니다.
    ///
    /// 이 차이가 전술을 바꿉니다 — 적을 빨리 갈아 내면 그만큼 빨리 다음 부대를 마주하게 되고,
    /// 반대로 내 부대를 아끼면 전장에 오래 남아 지원군이 늦게 들어옵니다.
    /// 파도에서는 없던 판단입니다.
    ///
    /// <b>왜 간격을 두는가</b>
    ///
    /// 자리만 보고 즉시 내보내면 앞 부대가 쓰러진 그 자리에 다음 부대가 튀어나옵니다.
    /// 눈으로 읽을 수 없고, 무엇보다 <b>이겼다는 감각</b>이 사라집니다.
    /// 짧은 간격을 두면 전선이 한 번 숨을 쉬고, 플레이어가 재배치할 여유가 생깁니다.
    ///
    /// MonoBehaviour에 의존하지 않는 순수 상태라 EditMode 테스트로 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class ReinforcementPool
    {
        // ====================================================================================================
        // 1. Fields
        // ====================================================================================================

        /// <summary>아직 전장에 나가지 않은 분대들입니다. 앞에서부터 나갑니다.</summary>
        private readonly Queue<SquadOrder> _waiting = new Queue<SquadOrder>();

        /// <summary>한 진영이 전장에 동시에 세울 수 있는 분대 수입니다.</summary>
        private readonly int _fieldCap;
        /// <summary>지원군이 올라오는 최소 간격(초)입니다.</summary>
        private readonly float _interval;

        /// <summary>다음 투입까지 남은 시간입니다.</summary>
        private float _cooldown;

        // ====================================================================================================
        // 2. Properties
        // ====================================================================================================

        /// <summary>아직 전장에 나가지 않은 분대 수입니다.</summary>
        public int Remaining => _waiting.Count;

        /// <summary>더 내보낼 분대가 없는지 여부입니다. 승리 판정의 입력입니다.</summary>
        public bool IsExhausted => _waiting.Count == 0;

        /// <summary>전장에 동시에 설 수 있는 분대 수입니다.</summary>
        public int FieldCap => _fieldCap;

        /// <summary>다음 투입까지 남은 시간입니다. HUD 표시에 씁니다.</summary>
        public float TimeUntilNext => Mathf.Max(0f, _cooldown);

        // ====================================================================================================
        // 3. Constructor
        // ====================================================================================================

        /// <summary>
        /// 전투 서열로 풀을 만듭니다.
        /// </summary>
        /// <param name="orderOfBattle">이 진영이 데려온 분대 전부입니다. 순서가 곧 투입 순서입니다.</param>
        /// <param name="fieldCap">전장에 동시에 설 수 있는 분대 수입니다. 1 미만이면 1로 봅니다.</param>
        /// <param name="interval">
        /// 투입 사이의 최소 간격(초)입니다. 첫 전개에는 적용되지 않습니다 —
        /// 전투가 시작되자마자 양측이 이미 서 있어야 합니다.
        /// </param>
        public ReinforcementPool(IReadOnlyList<SquadOrder> orderOfBattle, int fieldCap, float interval = 0f)
        {
            _fieldCap = Mathf.Max(1, fieldCap);
            _interval = Mathf.Max(0f, interval);

            if (orderOfBattle == null)
            {
                return;
            }

            for (int i = 0; i < orderOfBattle.Count; i++)
            {
                _waiting.Enqueue(orderOfBattle[i]);
            }
        }

        // ====================================================================================================
        // 4. Public Methods
        // ====================================================================================================

        /// <summary>
        /// 투입 간격을 진행시킵니다.
        ///
        /// 슬로우모션의 영향을 받도록 스케일된 시간으로 부릅니다.
        /// 명령을 내리는 동안 지원군이 그대로 밀려오면 슬로우모션이 곧 이득이 되어 버립니다.
        /// </summary>
        /// <param name="deltaTime">지난 시간입니다. 지원군 간격을 줄이는 데 씁니다.</param>
        public void Tick(float deltaTime)
        {
            if (_cooldown > 0f)
            {
                _cooldown -= deltaTime;
            }
        }

        /// <summary>
        /// 전장에 자리가 있으면 다음 분대를 내줍니다.
        ///
        /// <b>한 번에 하나씩</b> 내줍니다. 여러 자리가 비어 있어도 호출부가 반복해서 물어야 합니다.
        /// 그래야 간격 규칙이 분대마다 적용되고, 빈자리가 한꺼번에 메워지지 않습니다.
        /// </summary>
        /// <param name="squadsOnField">지금 전장에 서 있는 이 진영의 분대 수입니다.</param>
        /// <param name="order">내보낼 분대입니다.</param>
        /// <returns>내보낼 분대가 있으면 true입니다.</returns>
        public bool TryDeploy(int squadsOnField, out SquadOrder order)
        {
            order = default;

            if (_waiting.Count == 0 || squadsOnField >= _fieldCap || _cooldown > 0f)
            {
                return false;
            }

            order = _waiting.Dequeue();

            // 첫 전개에는 간격을 두지 않습니다. 전투가 시작되면 양측이 이미 서 있어야 합니다.
            // 그래서 간격은 <b>내보낸 뒤</b>에 겁니다.
            _cooldown = _interval;

            return true;
        }

        /// <summary>
        /// 전장을 상한까지 채웁니다. 전투 시작 시의 초기 전개에 씁니다.
        ///
        /// 간격을 무시하고 한 번에 채우므로, 시작하자마자 양측이 대치한 상태가 됩니다.
        /// </summary>
        /// <param name="squadsOnField">이미 서 있는 분대 수입니다. 보통 0입니다.</param>
        /// <param name="result">내보낼 분대들이 채워집니다. 호출 시 비워집니다.</param>
        /// <returns>내보낼 분대 수입니다.</returns>
        public int FillField(int squadsOnField, List<SquadOrder> result)
        {
            result.Clear();

            int room = _fieldCap - squadsOnField;

            while (room > 0 && _waiting.Count > 0)
            {
                result.Add(_waiting.Dequeue());
                room--;
            }

            // 초기 전개 직후부터 간격이 흐르게 합니다.
            _cooldown = _interval;

            return result.Count;
        }
    }
}
