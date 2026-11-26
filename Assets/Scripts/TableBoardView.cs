using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 씬에 배치된 카드 화면과 보상 프리팹에 테이블 상태 반영
    /// </summary>
    public sealed class TableBoardView : MonoBehaviour
    {
        /// <summary>
        /// 재료 종류에 대응하는 카드 그림 설정
        /// </summary>
        [System.Serializable]
        private struct MaterialAppearance
        {
            // 재료 식별자와 글자를 포함한 카드 원화
            [SerializeField] private MaterialKind material;
            [SerializeField] private Texture2D art;
            internal MaterialKind Material => material;
            internal Texture2D Art => art;
        }

        // 편집 모드에서 배치하는 테이블과 레일
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform table;
        [SerializeField] private RawImage[] slotFrames;
        [SerializeField] private RawImage[] locationCards;
        [SerializeField] private RawImage citizenCard;
        [SerializeField] private RectTransform cardLayer;
        [SerializeField] private Color activeSlotColor = new(0.64f, 0.87f, 0.96f, 1f);

        // 보상 카드의 시작 배치와 재사용 프리팹
        [SerializeField] private RectTransform[] rewardAnchors;
        [SerializeField] private RawImage rewardCardPrefab;
        [SerializeField] private MaterialAppearance[] materialAppearance;

        // 수색 및 일차 표시
        [SerializeField] private RectTransform searchTrack;
        [SerializeField] private RectTransform searchFill;
        [SerializeField] private Text dayNumber;
        private const float SearchTrackGap = 14f;

        // 레이아웃 스냅샷과 재료 카드별 실행 뷰
        private readonly Dictionary<MaterialKind, Texture2D> materialArt = new();
        private readonly Dictionary<int, RawImage> rewardCards = new();
        private TableLayout layout;
        private Color[] frameColors;


        // 씬 참조 검증과 테이블 좌표 스냅샷 생성
        internal bool TryInitialize(out TableLayout tableLayout)
        {
            tableLayout = null;

            // 고정 씬 오브젝트와 보상 프리팹 참조 확인
            if (canvas == null || table == null || citizenCard == null || cardLayer == null ||
                rewardCardPrefab == null || searchTrack == null || searchFill == null || dayNumber == null ||
                slotFrames == null || slotFrames.Length != TableLayout.SlotCount ||
                locationCards == null || locationCards.Length != TableLayout.SlotCount ||
                rewardAnchors == null || rewardAnchors.Length != TableLayout.RewardCount || materialAppearance == null)
            {
                return false;
            }

            // 재료 정의와 카드 그림을 일대일로 연결
            materialArt.Clear();

            foreach (var appearance in materialAppearance)
            {
                if (appearance.Art == null || !materialArt.TryAdd(appearance.Material, appearance.Art))
                {
                    return false;
                }
            }

            foreach (MaterialKind material in System.Enum.GetValues(typeof(MaterialKind)))
            {
                if (!materialArt.ContainsKey(material))
                {
                    return false;
                }
            }

            // Hierarchy의 슬롯 위치와 카드 위치를 같은 좌표계로 가져옴
            var bounds = new Rect[slotFrames.Length];
            var locations = new Vector2[slotFrames.Length];
            frameColors = new Color[slotFrames.Length];

            for (var slot = 0; slot < slotFrames.Length; slot++)
            {
                if (slotFrames[slot] == null || locationCards[slot] == null)
                {
                    return false;
                }

                var frame = slotFrames[slot].rectTransform;
                bounds[slot] = new Rect(GetTablePosition(frame), frame.rect.size);
                locations[slot] = GetTablePosition(locationCards[slot].rectTransform);
                frameColors[slot] = slotFrames[slot].color;
            }

            var rewards = new Vector2[rewardAnchors.Length];

            for (var index = 0; index < rewards.Length; index++)
            {
                if (rewardAnchors[index] == null)
                {
                    return false;
                }

                rewards[index] = GetTablePosition(rewardAnchors[index]);
            }

            // 화면과 드롭 판정에 동일한 편집 모드 좌표 사용
            var citizen = citizenCard.rectTransform;
            layout = new TableLayout(table.rect.size, citizen.rect.size, GetTablePosition(citizen), bounds, locations, rewards);
            tableLayout = layout;
            return true;
        }


        // 마우스 화면 좌표를 테이블 기준 좌표로 변환
        internal Vector2 GetBoardPoint(Vector2 screenPoint)
        {
            // 실제 캔버스 크기와 피벗을 반영한 좌상단 기준 좌표
            RectTransformUtility.ScreenPointToLocalPointInRectangle(table, screenPoint, null, out var local);
            return new Vector2(local.x - table.rect.xMin, table.rect.yMax - local.y);
        }


        // 컴포넌트 활성 상태에 화면 표시 연동
        internal void SetVisible(bool isVisible)
        {
            canvas.enabled = isVisible;
        }


        // 레일과 시민 및 재료 보상을 화면에 반영
        internal void RenderBoard(TableBoard board)
        {
            // 고정 슬롯의 카드 면을 모델의 현재 장소로 갱신
            for (var slot = 0; slot < locationCards.Length; slot++)
            {
                var location = board.GetLocation(slot);
                var card = locationCards[slot];
                card.gameObject.SetActive(location != null);

                if (location != null)
                {
                    card.texture = slot == TableLayout.PreviewSlot ? location.Definition.PreviewArt : location.Definition.CardArt;
                }
            }

            // 이미 배치된 시민을 이동하고 새 재료만 프리팹에서 생성
            var searchSlot = board.SearchSlot;

            foreach (var card in board.TableCards)
            {
                if (!card.Material.HasValue)
                {
                    var position = card.Id == board.SearchCitizenId
                        ? layout.GetLocationPosition(searchSlot) + TableLayout.SearchOffset
                        : card.Position;
                    SetPosition(citizenCard.rectTransform, position);
                    continue;
                }

                if (!rewardCards.TryGetValue(card.Id, out var reward))
                {
                    reward = Instantiate(rewardCardPrefab, cardLayer, false);
                    reward.texture = materialArt[card.Material.Value];
                    reward.name = $"{card.Material.Value} {card.Id}";
                    rewardCards.Add(card.Id, reward);
                }

                SetPosition(reward.rectTransform, card.Position);
                reward.rectTransform.SetAsLastSibling();
            }

            // 수색 중인 시민을 슬롯 위에 표시
            citizenCard.rectTransform.SetAsLastSibling();
            HighlightSlot(searchSlot);
            dayNumber.text = board.Day.ToString();
            searchTrack.gameObject.SetActive(searchSlot >= 0);

            if (searchSlot >= 0)
            {
                var location = layout.GetLocationPosition(searchSlot);
                var offset = layout.CardSize.y + TableLayout.SearchOffset.y + SearchTrackGap;
                SetPosition(searchTrack, location + Vector2.up * offset);
                RenderProgress(board.SearchProgress);
            }
        }


        // 확정 전 드래그 위치와 유효한 슬롯 강조 표시
        internal void RenderDrag(int cardId, Vector2 position, int targetSlot)
        {
            // 재료 뷰가 없으면 Hierarchy에 배치된 시민 사용
            var image = rewardCards.TryGetValue(cardId, out var reward) ? reward : citizenCard;
            SetPosition(image.rectTransform, position);
            image.rectTransform.SetAsLastSibling();
            HighlightSlot(targetSlot);
        }


        // 수색 진행률을 편집된 막대 너비에 적용
        internal void RenderProgress(float fraction)
        {
            searchFill.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, searchTrack.rect.width * Mathf.Clamp01(fraction));
        }


        // 수색할 수 있는 실선 슬롯의 테두리 색 갱신
        private void HighlightSlot(int highlightedSlot)
        {
            for (var slot = 0; slot < TableLayout.PreviewSlot; slot++)
            {
                slotFrames[slot].color = slot == highlightedSlot ? activeSlotColor : frameColors[slot];
            }
        }


        // 씬에 배치된 요소의 상단 왼쪽을 테이블 좌표로 조회
        private Vector2 GetTablePosition(RectTransform element)
        {
            var corner = element.TransformPoint(new Vector3(element.rect.xMin, element.rect.yMax));
            var local = table.InverseTransformPoint(corner);
            return new Vector2(local.x - table.rect.xMin, table.rect.yMax - local.y);
        }


        // 테이블 원점에 정렬한 레이어에 카드 위치 적용
        private static void SetPosition(RectTransform element, Vector2 position)
        {
            element.anchoredPosition = new Vector2(position.x, -position.y);
        }
    }
}
