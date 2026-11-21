# ClipPlus

Windows 11 클립보드 기록(Win+V)에 키보드 단축키를 추가하는 작은 트레이 프로그램입니다.

| 입력 | 동작 |
|---|---|
| Insert | 선택 항목 고정·해제 |
| Delete 한 번 | 0.2초 뒤 선택한 비고정 항목 삭제 |
| Delete를 0.2초 안에 두 번 | 고정 항목을 제외한 이력 모두 삭제 |
| Win+V를 0.2초 안에 두 번 | ClipPlus 새로고침 |
| 트레이 → 새로고침 | 작업 프로세스와 키보드 훅 갱신 |

전체 삭제 후 남은 목록은 첫 항목부터 표시합니다. 단축키는 실제 클립보드 목록의 선택·창·포커스를 확인한 경우에만 처리하며, 키 자동 반복은 두 번 누르기로 세지 않습니다.

## 빌드

Windows x64, .NET Framework 4.8, Windows 10/11 SDK의 `Windows.winmd`, Roslyn C# 컴파일러가 필요합니다. 해당 Windows 설치에 `windowsudk.winmd`도 있어야 합니다.

```powershell
.\Build.ps1 -Compiler 'C:\path\to\Roslyn\csc.exe'
```

결과는 `artifacts`에 생성됩니다. SDK 메타데이터와 Windows UDK 메타데이터는 로컬 설치에서 읽으며 저장소에 재배포하지 않습니다. 필요하면 `-WindowsMetadata`, `-UdkMetadata`, `-OutDir`를 지정할 수 있습니다.

실행 시 `ClipPlus.exe`와 `windowsudk.winmd`를 같은 폴더에 두십시오. `ClipPlus.exe`를 실행하면 감독 프로세스가 트레이와 작업 프로세스를 관리합니다. 프로그램을 종료해도 감독자가 다시 실행하므로 완전히 중지하려면 감독 프로세스와 자동 시작 작업도 중지해야 합니다.

로그온 자동 시작에는 `ClipPlusKeepAlive.vbs`를 실행하는 작업 스케줄러 작업을 사용할 수 있습니다. VBS와 실행 파일은 같은 폴더에 두십시오. 트레이의 종료는 자동 재실행 설정을 해제하지 않습니다.

## 검사

```powershell
.\Tests\Run.ps1 -Compiler 'C:\path\to\Roslyn\csc.exe'
```

기본 검사는 모의 클립보드와 별도 작업 프로세스를 사용하며 실제 클립보드를 삭제하지 않습니다. 실제 클립보드를 변경하는 검사는 `Tests/Manual`에 별도로 둡니다.

## 구현과 호환성

고정은 Windows UDK 데이터 API, 삭제·전체 삭제는 전용 STA의 Windows 클립보드 API를 사용합니다. UI Automation은 현재 선택을 읽고 전체 삭제 후 첫 행을 표시하는 데 사용합니다. 고정 아이콘 Invoke, 마우스·키 입력 합성, Windows 입력 호스트 강제 종료는 사용하지 않습니다.

Windows UDK는 내부 API이므로 Windows 업데이트 후 호환성을 다시 확인해야 합니다. 고정 기능에 필요한 API나 메타데이터가 없는 Windows 버전에서는 작동하지 않을 수 있습니다. 로그는 `%LOCALAPPDATA%\ClipPlus\activity.log`에 기록하며 클립보드 본문은 기록하지 않습니다.
