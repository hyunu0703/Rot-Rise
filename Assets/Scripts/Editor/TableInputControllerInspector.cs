using UnityEditor;

namespace RotRise.Gameplay
{
    /// <summary>
    /// 플레이 중 보드 원본 상태를 읽기 전용으로 표시
    /// </summary>
    [CustomEditor(typeof(TableInputController))]
    internal sealed class TableInputControllerInspector : Editor
    {
        // Inspector에 표시하는 네 개의 장소 슬롯
        private const int RailSlotCount = 4;


        // 기본 씬 설정과 플레이 상태 표시
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // 플레이 초기화가 끝난 보드에서만 실행 상태 조회
            var gameBoard = (TableInputController)target;

            if (!gameBoard.IsReady)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("일차", gameBoard.CurrentDay.ToString());
            EditorGUILayout.LabelField("재료 카드", gameBoard.MaterialCount.ToString());

            for (var slot = 0; slot < RailSlotCount; slot++)
            {
                var title = slot == RailSlotCount - 1 ? $"{slot + 1}번 · 예고" : $"{slot + 1}번";
                EditorGUILayout.LabelField(title, gameBoard.GetLocationLabel(slot));
            }

            if (gameBoard.SearchSlot != 0)
            {
                EditorGUILayout.LabelField("수색 진행", $"{gameBoard.SearchSlot}번 · {gameBoard.SearchProgress:P0}");
            }
        }


        // 플레이 중 진행도와 일차를 주기적으로 갱신
        public override bool RequiresConstantRepaint()
        {
            return EditorApplication.isPlaying;
        }
    }
}
