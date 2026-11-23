// Keeps the conversation in view.
//  - While the reader is near the bottom, new content (streaming text, tool cards) keeps the view pinned to the bottom.
//  - If the reader scrolled up to read, it leaves them alone.
//  - A pending approval is always brought into view, because the turn is waiting for a click on it.
//  - Sending a message jumps to the bottom.
(function () {
    const NEAR_BOTTOM_PX = 160;
    let stick = true;

    const scroller = () => document.querySelector('.sc-ai-chat-page__body');

    function toBottom() {
        const el = scroller();
        if (el) el.scrollTop = el.scrollHeight;
    }

    // Track whether the reader is near the bottom (scroll events do not bubble, so listen in the capture phase).
    document.addEventListener('scroll', function (e) {
        const el = scroller();
        if (el && e.target === el) {
            stick = el.scrollHeight - el.scrollTop - el.clientHeight < NEAR_BOTTOM_PX;
        }
    }, true);

    new MutationObserver(function (mutations) {
        const el = scroller();
        if (!el) return;

        // The reader just sent a message: follow the conversation again.
        for (const m of mutations) {
            for (const n of m.addedNodes) {
                if (n.nodeType === 1 && (n.matches?.('.sc-ai-message--user') || n.querySelector?.('.sc-ai-message--user'))) {
                    stick = true;
                }
            }
        }

        // A pending approval must be visible: it is the only thing the conversation is waiting for.
        const pending = el.querySelector('.gate-card[data-status="Pending"]');
        if (pending && !pending.dataset.revealed) {
            pending.dataset.revealed = '1';
            stick = true;
        }

        if (stick) toBottom();
    }).observe(document.body, { childList: true, subtree: true, characterData: true });
})();
