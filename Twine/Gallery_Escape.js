
/* ===== GALLERY ESCAPE: 키보드 이동 + 그림 확대 ===== */

// 입력칸에 글자를 치는 중이거나 대화창이 열려 있으면 단축키를 쓰지 않는다
function galleryKeysBlocked(ev) {
  var el = ev.target;
  var tag = el && el.tagName ? el.tagName.toLowerCase() : "";
  return tag === "input" || tag === "textarea" || tag === "select" || (el && el.isContentEditable) || Dialog.isOpen();
}

// --- 1. WASD 이동: #nav-up / #nav-left / #nav-down / #nav-right 버튼을 누른다 ---
// e.code(키 위치)를 써서 한글 입력 상태(W → ㅈ)에서도 동작
var GALLERY_NAV_KEYS = { KeyW: "#nav-up", KeyA: "#nav-left", KeyS: "#nav-down", KeyD: "#nav-right" };

$(document).on("keydown", function (ev) {
  if (galleryKeysBlocked(ev) || ev.ctrlKey || ev.altKey || ev.metaKey) return;
  if ($("#zoom-overlay").length) return;               // 확대 중에는 이동하지 않음
  var sel = GALLERY_NAV_KEYS[ev.code];
  if (!sel) return;
  var $btn = $("#passages " + sel);
  if (!$btn.length || $btn.hasClass("disabled")) return;
  ev.preventDefault();
  $btn.addClass("pressed");
  $btn.trigger("click");
});

// --- 2. 그림 확대(Zoom) 토글: E / Space / 그림 클릭 ---
function galleryZoomOut() {
  var $ov = $("#zoom-overlay");
  if (!$ov.length) return false;
  $ov.removeClass("open");
  setTimeout(function () { $ov.remove(); }, 220);
  $("body").removeClass("zooming");
  return true;
}

function galleryZoomIn($img) {
  if (!$img || !$img.length) return false;
  var $ov = $('<div id="zoom-overlay" role="dialog" aria-label="그림 확대"></div>');
  $ov.append($("<img>").attr({ src: $img.attr("src"), alt: $img.attr("alt") || "" }));
  $ov.append('<div class="zoom-hint">E · Space · 클릭 : 축소</div>');
  $("body").append($ov).addClass("zooming");
  requestAnimationFrame(function () { $ov.addClass("open"); });
  return true;
}

function galleryToggleZoom() {
  if (galleryZoomOut()) return true;
  return galleryZoomIn($("#passages .artwork img").first());
}

$(document).on("keydown", function (ev) {
  if (galleryKeysBlocked(ev)) return;
  if (ev.code === "KeyE" || ev.code === "Space") {
    // 그림이 있는 패시지(그림 가까이)에서만 동작
    if (!$("#zoom-overlay").length && !$("#passages .artwork").length) return;
    ev.preventDefault();                                 // Space가 페이지를 스크롤하지 않게
    galleryToggleZoom();
  } else if (ev.code === "Escape") {
    galleryZoomOut();
  }
});

// 그림 클릭 → 확대, 확대 화면 클릭 → 축소
$(document).on("click", "#passages .artwork img", function () { galleryZoomIn($(this)); });
$(document).on("click", "#zoom-overlay", function () { galleryZoomOut(); });

// 패시지가 바뀌면 확대 화면 정리
$(document).on(":passagestart", function () { $("#zoom-overlay").remove(); $("body").removeClass("zooming"); });
