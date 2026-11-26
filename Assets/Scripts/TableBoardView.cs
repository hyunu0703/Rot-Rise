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

        /// <summary>
        /// 장소에서 착지점까지 움직이는 보상 카드의 화면 상태
        /// </summary>
        private sealed class RewardMotion
        {
            // 이동 대상과 출발 위치 및 모델의 착지 좌표
            internal int CardId { get; }
            internal RectTransform Image { get; }
            internal Vector2 Origin { get; }
            internal Vector2 Destination { get; }
            internal float Duration { get; }
            internal float Elapsed { get; set; }


            // 보상 배치에서 이번 이동의 양 끝과 시간을 기록
            internal RewardMotion(int cardId, RectTransform image, Vector2 origin, Vector2 destination, float duration)
            {
                // 모델의 착지 좌표와 화면 표시 대상 연결
                CardId = cardId;
                Image = image;
                Origin = origin;
                Destination = destination;
                Duration = duration;
            }
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

        // 레이아웃 스냅샷과 테이블 카드별 실행 뷰
        private readonly Dictionary<MaterialKind, Texture2D> materialArt = new();
        private readonly Dictionary<int, RawImage> tableCardImages = new();
        private TableLayout layout;
        private Color[] frameColors;

        // 드래그 중 유효한 결합 대상의 외곽 표시
        private const float StackHighlightWidth = 4f;
        private RectTransform stackHighlight;

        // 보상 카드 확산 연출과 카드별 이동 시간
        private const float RewardTravelDuration = 0.48f;
        private const float RewardTravelInterval = 0.06f;
        private const float RewardArcHeight = 20f;
        private readonly List<RewardMotion> rewardMotions = new();


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
            // 확정 배치 재표시 시 드래그 대상 강조 해제
            HighlightStack(null);

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

            // 카드 종류와 관계없이 모델의 더미 순서대로 화면에 배치
            var searchSlot = board.SearchSlot;

            foreach (var card in board.TableCards)
            {
                var image = GetOrCreateTableCardImage(card);
                var position = card.Id == board.SearchCitizenId
                    ? layout.GetLocationPosition(searchSlot) + TableLayout.SearchOffset
                    : card.Position;

                // 보드 재표시 중에도 이동 중인 보상의 현재 좌표 유지
                if (CanInteractWithCard(card.Id))
                {
                    SetPosition(image.rectTransform, position);
                }

                image.rectTransform.SetAsLastSibling();
            }

            // 수색 중인 시민을 슬롯 위에 표시
            if (searchSlot >= 0)
            {
                tableCardImages[board.SearchCitizenId].rectTransform.SetAsLastSibling();
            }

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


        // 수색 완료 장소에서 방금 지급한 재료 카드의 이동 시작
        internal void PlayRewardScatter(IReadOnlyList<TableBoard.TableCard> cards, int completedSlot)
        {
            // 모델이 마지막에 추가한 보상 묶음과 완료한 장소 좌표 연결
            var origin = layout.GetLocationPosition(completedSlot);
            var firstReward = cards.Count - TableLayout.RewardCount;

            for (var index = firstReward; index < cards.Count; index++)
            {
                var card = cards[index];
                var image = GetOrCreateTableCardImage(card).rectTransform;
                var duration = RewardTravelDuration + RewardTravelInterval * (index - firstReward);
                SetPosition(image, origin);
                rewardMotions.Add(new RewardMotion(card.Id, image, origin, card.Position, duration));
            }
        }


        // 이동 중인 보상의 클릭 및 겹치기 대상 여부 조회
        internal bool CanInteractWithCard(int cardId)
        {
            // 보이는 위치와 모델의 착지 위치가 일치한 뒤 상호작용 허용
            foreach (var motion in rewardMotions)
            {
                if (motion.CardId == cardId)
                {
                    return false;
                }
            }

            return true;
        }


        // 보상 카드의 완만한 곡선 이동과 착지 처리
        internal void AdvanceRewardScatter(float deltaTime)
        {
            // 이동이 끝난 항목을 역순 순회 중 제거
            for (var index = rewardMotions.Count - 1; index >= 0; index--)
            {
                var motion = rewardMotions[index];
                motion.Elapsed += deltaTime;

                if (motion.Elapsed >= motion.Duration)
                {
                    SetPosition(motion.Image, motion.Destination);
                    rewardMotions.RemoveAt(index);
                    continue;
                }

                // 출발 직후 퍼지고 착지 직전에 속도가 줄어드는 곡선 보간
                var remaining = 1f - motion.Elapsed / motion.Duration;
                var progress = 1f - remaining * remaining;
                var lift = Mathf.Sin(progress * Mathf.PI) * RewardArcHeight;
                var position = Vector2.Lerp(motion.Origin, motion.Destination, progress) - Vector2.up * lift;
                SetPosition(motion.Image, position);
            }
        }


        // 카드 식별자에 대응하는 화면 오브젝트 확보
        private RawImage GetOrCreateTableCardImage(TableBoard.TableCard card)
        {
            // 기존 카드 오브젝트의 위치와 이름 재사용
            if (tableCardImages.TryGetValue(card.Id, out var image))
            {
                return image;
            }

            // 첫 시민은 씬 배치 사용, 새 카드는 종류별 원본에서 복제
            if (card.Id == TableBoard.StartingCitizenId)
            {
                image = citizenCard;
            }
            else if (card.Material.HasValue)
            {
                image = Instantiate(rewardCardPrefab, cardLayer, false);
                image.texture = materialArt[card.Material.Value];
                image.name = $"{card.Material.Value} {card.Id}";
            }
            else
            {
                image = Instantiate(citizenCard, cardLayer, false);
                image.name = $"Citizen {card.Id}";
            }

            // 카드 선택과 드래그에 사용할 식별자 등록
            tableCardImages.Add(card.Id, image);
            return image;
        }


        // 확정 전 드래그 위치와 유효한 슬롯 강조 표시
        internal void RenderDrag(IReadOnlyList<TableBoard.TableCard> cards, Vector2 delta, int targetSlot, Rect? targetBounds)
        {
            // 집은 카드와 위쪽 카드의 간격 및 그리는 순서 유지
            foreach (var card in cards)
            {
                var image = tableCardImages[card.Id];
                SetPosition(image.rectTransform, card.Position + delta);
                image.rectTransform.SetAsLastSibling();
            }

            // 현재 수색 중이거나 투입 가능한 레일 위치 강조
            HighlightSlot(targetSlot);
            HighlightStack(targetBounds);
        }


        // 결합 가능한 더미의 바깥 테두리 표시
        private void HighlightStack(Rect? targetBounds)
        {
            // 드롭이나 취소로 대상이 없어진 경우 강조 숨김
            if (!targetBounds.HasValue)
            {
                if (stackHighlight != null)
                {
                    stackHighlight.gameObject.SetActive(false);
                }

                return;
            }

            // 처음 필요한 순간에 테이블 위 강조선 생성
            if (stackHighlight == null)
            {
                var highlight = new GameObject("Stack Drop Highlight", typeof(RectTransform));
                stackHighlight = (RectTransform)highlight.transform;
                stackHighlight.SetParent(table, false);
                stackHighlight.anchorMin = new Vector2(0f, 1f);
                stackHighlight.anchorMax = stackHighlight.anchorMin;
                stackHighlight.pivot = stackHighlight.anchorMin;
                AddHighlightEdge(new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -StackHighlightWidth));
                AddHighlightEdge(Vector2.zero, Vector2.right, new Vector2(0f, StackHighlightWidth));
                AddHighlightEdge(Vector2.zero, Vector2.up, new Vector2(StackHighlightWidth, 0f));
                AddHighlightEdge(Vector2.right, Vector2.one, new Vector2(-StackHighlightWidth, 0f));
            }

            // 카드에 가려지지 않도록 더미 외곽 바깥에 표시
            var bounds = targetBounds.Value;
            var border = Vector2.one * StackHighlightWidth;
            SetPosition(stackHighlight, bounds.position - border);
            stackHighlight.sizeDelta = bounds.size + border * 2f;
            stackHighlight.SetAsLastSibling();
            stackHighlight.gameObject.SetActive(true);
        }


        // 레이아웃 크기에 맞춰 늘어나는 강조선 추가
        private void AddHighlightEdge(Vector2 anchorMin, Vector2 anchorMax, Vector2 inward)
        {
            // 테두리에서 안쪽으로 두께를 확보하고 입력은 카드에 전달
            var edge = new GameObject("Edge", typeof(RectTransform), typeof(Image));
            var image = edge.GetComponent<Image>();
            var rect = image.rectTransform;
            rect.SetParent(stackHighlight, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.Min(inward, Vector2.zero);
            rect.offsetMax = Vector2.Max(inward, Vector2.zero);
            image.color = activeSlotColor;
            image.raycastTarget = false;
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
