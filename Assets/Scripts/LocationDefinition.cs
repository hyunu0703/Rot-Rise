using System;
using UnityEngine;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 수색 보상에 사용하는 공통 재료 종류
    /// </summary>
    internal enum MaterialKind
    {
        Wood,
        Scrap,
        Cloth,
        Plastic,
        Plate,
        Wire,
        MachinePart,
        ElectronicPart,
        GunPart
    }

    /// <summary>
    /// 같은 종류의 장소 카드가 공유하는 표시와 재료 추첨 설정
    /// </summary>
    [Serializable]
    internal sealed class LocationDefinition
    {
        /// <summary>
        /// 재료 한 종류의 추첨 가중치 항목
        /// </summary>
        [Serializable]
        private struct RewardWeight
        {
            // 기획서의 재료 및 단일 추첨 확률
            [SerializeField] private MaterialKind material;
            [SerializeField] private int weight;
            internal MaterialKind Material => material;
            internal int Weight => weight;
        }

        // 장소 정의와 재료 추첨 표
        private const int WeightTotal = 100;
        [SerializeField] private string displayName;
        [SerializeField] private Texture2D cardArt;
        [SerializeField] private Texture2D previewArt;
        [SerializeField] private RewardWeight[] rewards;

        internal string DisplayName => displayName;
        internal Texture2D CardArt => cardArt;
        internal Texture2D PreviewArt => previewArt;


        // 플레이 시작 전에 장소 이름과 보상 합계 검증
        internal bool HasValidRewards()
        {
            // 장소 표의 빈 입력 차단
            if (string.IsNullOrWhiteSpace(displayName) || cardArt == null || previewArt == null || rewards == null || rewards.Length == 0)
            {
                return false;
            }

            var total = 0;
            var seenMaterials = new System.Collections.Generic.HashSet<MaterialKind>();

            // 중복 종류와 범위를 벗어난 가중치 차단
            foreach (var reward in rewards)
            {
                if (reward.Weight < 0 || reward.Weight > WeightTotal || !seenMaterials.Add(reward.Material))
                {
                    return false;
                }

                total += reward.Weight;
            }

            return total == WeightTotal;
        }


        // 장소 가중치로 재료 한 장 추첨
        internal MaterialKind DrawMaterial()
        {
            // 매 호출마다 독립 난수 사용
            var roll = UnityEngine.Random.Range(0, WeightTotal);

            foreach (var reward in rewards)
            {
                // 가중치가 0인 항목은 당첨 구간 없음
                if (roll < reward.Weight)
                {
                    return reward.Material;
                }

                roll -= reward.Weight;
            }

            throw new InvalidOperationException($"Invalid reward weights for {displayName}");
        }
    }
}
