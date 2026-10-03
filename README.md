# Racing

Unity 6 (6000.3.11f1, URP) 3D 서킷 레이싱 게임 프로토타입.

## 게임 내용
- 약 1.85 km 도심 시가지 서킷 (직각 코너 10개, 콘크리트 방호벽·광고판, 보도, 가로등, 빌딩 숲, 출발 게이트·관중석)
- 플레이어 1명 + AI 5명, 1~10랩 선택 (기본 3랩)
- 아케이드 레이캐스트 서스펜션 차량 물리 (핸드브레이크 드리프트, 잔디 감속)
- 순위/랩/랩타임/베스트랩 HUD, 미니맵, 역주행 경고, 카운트다운, 결과 화면
- 합성 엔진음 (5단 기어 피치)

## 조작
| 동작 | 키보드 | 게임패드 |
|---|---|---|
| 가속 / 브레이크·후진 | W·↑ / S·↓ | RT / LT |
| 조향 | A D · ← → | 왼쪽 스틱 |
| 핸드브레이크 | Space | A |
| 차량 리셋 | R | Y |
| 카메라 전환 | C | RB |
| 일시정지 | ESC | Select |
| 메뉴: 시작 / 랩 수 | Enter / ← → | Start·A / 방향패드 |

## 열기 / 빌드
1. Unity Hub에서 이 폴더를 6000.3.11f1로 연다.
2. `Assets/_Project/Scenes/Race.unity`를 열고 Play.
3. 메뉴 **Racing → Setup Scene**: 씬·머티리얼을 코드(`Assets/_Project/Editor/RacingSetup.cs`)로 다시 생성.
4. 메뉴 **Racing → Build Windows**: `Builds/Windows/Racing.exe` 생성.

트랙 모양은 `RacingSetup.StreetCorners`(도시 블록 꼭짓점, 코너는 자동으로 둥글게 처리)로 정해지며, 도로·방호벽·빌딩은 `TrackBuilder`가 실행 시 생성한다.

## 개발용 실행 옵션
`-autostart -autopilot -laps N -timescale N -shots <폴더> -shottimes 5,20 -quitafter 60`
