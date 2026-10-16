using DiePipeline.Wafer.Domain;

namespace DiePipeline.Wafer.Execution;

/// <summary>
/// 웨이퍼 검사가 지금 어디까지 왔나 — 화면이 단계 글자 · 진행 막대 · 맵 칸을 그때그때 칠한다(교수님 · 동기 <c>WaferEvents</c> 자리).
///
/// ★ 결과에는 섞지 않는다. 알림은 지켜보는 사람용이고, 결과는 끝난 뒤의 것 하나다 — 알림을 안 받아도 결과는 같다.
/// ★ 시각 · 걸린 ms 처럼 실행마다 다른 값은 여기에도 안 싣는다(필요하면 받는 쪽이 잰다).
/// </summary>
public abstract record WaferProgress;

/// <summary>격자를 놓았다(정렬이 끝났다). 아직 die 는 안 봤다 — 화면이 빈 맵을 미리 그린다.</summary>
public sealed record WaferGridReady(DieGrid Grid) : WaferProgress;

/// <summary>
/// die 하나를 끝냈다. <paramref name="Done"/> = 지금까지 끝낸 칸 수, <paramref name="Total"/> = 격자 칸 수(원판 밖 칸 포함 — 맵 칸 수와 같다).
/// </summary>
public sealed record WaferDieDone(DieInspection Die, int Done, int Total) : WaferProgress;