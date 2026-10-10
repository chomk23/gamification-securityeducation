// WebGL 전용 — 보안 레이싱 HTML을 DOM iframe으로 띄우는 브릿지.
// RacingWebViewBridge.cs가 DllImport("__Internal")로 호출한다.
mergeInto(LibraryManager.library, {

  RacingIframe_Show: function (urlPtr) {
    var url = UTF8ToString(urlPtr);
    var f = document.getElementById('racing-iframe');
    if (!f) {
      f = document.createElement('iframe');
      f.id = 'racing-iframe';
      f.setAttribute('allow', 'autoplay');
      f.style.cssText =
        'position:fixed;left:50%;top:50%;transform:translate(-50%,-50%);' +
        'width:min(1280px,95vw);height:min(720px,90vh);' +
        'border:2px solid #a78bfa;background:#000;z-index:9999;';

      // 레이싱 HTML → postMessage 수신 (최초 1회 등록)
      //   {type:'securityRaceFinished', rank} — 레이스 종료(rank 0 = 미완주)
      //   {type:'securityRaceCancel'}         — 닫기 버튼 / ESC
      window.addEventListener('message', function (e) {
        // 레이싱 iframe이 보낸 메시지만 처리 — 콘솔이나 다른 프레임이 보낸 위조 결과 차단
        if (e.source !== f.contentWindow) return;
        var d = e.data;
        if (!d) return;
        try {
          if (d.type === 'securityRaceFinished')
            SendMessage('RacingWebViewBridge', 'OnRaceFinishedFromJs', d.rank | 0);
          else if (d.type === 'securityRaceCancel')
            SendMessage('RacingWebViewBridge', 'OnRaceCancelFromJs');
        } catch (err) { console.warn('[RacingIframe] SendMessage 실패:', err); }
      });
    }

    // 전체 화면 중에는 전체 화면 요소와 그 자손만 그려진다(z-index 무관).
    // 템플릿은 #unity-container를 전체 화면으로 올리므로 처음부터 그 안에 붙여, 레이스 도중 전체 화면을 켜도 보이게 한다
    // (iframe을 옮기면 페이지가 다시 로드되어 레이스가 초기화되므로 표시 중에는 옮기지 않는다).
    // 캔버스는 자식을 그리지 않으므로 캔버스 전체 화면이면 풀고, 다른 요소면 그 안에 붙인다.
    var fs = document.fullscreenElement || document.webkitFullscreenElement;
    var parent = document.getElementById('unity-container') || document.body;
    if (fs) {
      if (fs.tagName === 'CANVAS') {
        try {
          var exitFs = document.exitFullscreen || document.webkitExitFullscreen;
          var pending = exitFs && exitFs.call(document);
          if (pending && pending.catch) pending.catch(function () {});
        } catch (e) {}
      } else if (!fs.contains(parent)) {
        parent = fs;
      }
    }
    if (f.parentNode !== parent) parent.appendChild(f);

    f.src = url;
    f.style.display = 'block';
    setTimeout(function () { try { f.contentWindow.focus(); } catch (e) {} }, 150);
  },

  RacingIframe_Hide: function () {
    var f = document.getElementById('racing-iframe');
    if (f) {
      f.style.display = 'none';
      f.src = 'about:blank';
    }
    // 포커스를 Unity 캔버스로 복귀 (키 입력 재개)
    try {
      var c = document.querySelector('#unity-canvas') || document.querySelector('canvas');
      if (c) c.focus();
    } catch (e) {}
  }
});
