window.SmartPanelCameras = (function () {
    let _pc = null;
    let _videoId = null;
    let _fallbackTimer = null;

    // --- WHEP with auto-fallback to iframe ---
    async function startWhep(videoElementId, whepUrl, iframeUrl) {
        stop();
        const video = document.getElementById(videoElementId);
        if (!video) return;

        // After 4s with no frames → use proven iframe player
        if (iframeUrl) {
            _fallbackTimer = setTimeout(() => {
                if (!video.videoWidth) {
                    _replaceWithIframe(video, iframeUrl);
                }
            }, 4000);
        }

        try {
            const pc = new RTCPeerConnection({ iceServers: [] });
            _pc = pc;
            _videoId = videoElementId;

            pc.addTransceiver('video', { direction: 'recvonly' });
            pc.addTransceiver('audio', { direction: 'recvonly' });

            const offer = await pc.createOffer();
            await pc.setLocalDescription(offer);

            const resp = await fetch(whepUrl, {
                method: 'POST',
                headers: { 'Content-Type': 'application/sdp' },
                body: pc.localDescription.sdp
            });

            if (!resp.ok) {
                // WHEP failed immediately → use iframe right away
                if (iframeUrl) {
                    clearTimeout(_fallbackTimer);
                    _fallbackTimer = null;
                    _replaceWithIframe(video, iframeUrl);
                }
                return;
            }

            const sdp = await resp.text();
            await pc.setRemoteDescription({ type: 'answer', sdp });

            pc.ontrack = (e) => {
                if (!e.streams[0]) return;
                clearTimeout(_fallbackTimer);
                _fallbackTimer = null;
                video.srcObject = e.streams[0];
                video.muted = true;
                video.play().catch(err => console.warn('camera play:', err));

                // Minimize jitter buffer for lowest latency
                if (typeof e.receiver?.jitterBufferTarget !== 'undefined') {
                    e.receiver.jitterBufferTarget = 0;
                }
            };

            pc.onconnectionstatechange = () => {
                if (pc.connectionState === 'failed' || pc.connectionState === 'disconnected') {
                    if (iframeUrl && !video.videoWidth) {
                        clearTimeout(_fallbackTimer);
                        _replaceWithIframe(video, iframeUrl);
                    }
                }
            };
        } catch (err) {
            console.warn('SmartPanel WHEP error:', err);
            clearTimeout(_fallbackTimer);
            if (iframeUrl) _replaceWithIframe(video, iframeUrl);
        }
    }

    function _replaceWithIframe(video, iframeUrl) {
        if (!video || !iframeUrl) return;
        const parent = video.parentElement;
        if (!parent) return;
        // Remove video, insert iframe
        const iframe = document.createElement('iframe');
        iframe.id = video.id;
        iframe.src = iframeUrl + '?autoplay=1&muted=1&playsinline=1';
        iframe.className = video.className;
        iframe.style.cssText = 'border:0;width:100%;height:100%;background:#041225;';
        iframe.allow = 'autoplay; fullscreen';
        parent.replaceChild(iframe, video);
    }

    function stop() {
        clearTimeout(_fallbackTimer);
        _fallbackTimer = null;
        if (_pc) { try { _pc.close(); } catch (_) { } _pc = null; }
        if (_videoId) {
            const el = document.getElementById(_videoId);
            if (el && el.tagName === 'VIDEO') el.srcObject = null;
            _videoId = null;
        }
    }

    // --- PTZ: direct fetch from browser, zero Blazor SignalR ---
    function ptzDown(event, apiBase, entityId) {
        const btn = event.target.closest('[data-ptz]');
        if (!btn) return;
        event.preventDefault();
        ptzSend(apiBase, entityId, btn.dataset.ptz);
    }

    function ptzUp(event, apiBase, entityId) {
        if (event.target.closest('[data-ptz]')) {
            ptzSend(apiBase, entityId, 'stop');
        }
    }

    function ptzLeave(apiBase, entityId) {
        ptzSend(apiBase, entityId, 'stop');
    }

    function ptzSend(apiBase, entityId, command) {
        fetch(apiBase.replace(/\/$/, '') + '/api/cameras/control', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ entityId, command, step: 0.11 })
        }).catch(() => { });
    }

    function stopWhep() { stop(); }

    return { startWhep, stopWhep, ptzDown, ptzUp, ptzLeave, ptzSend };
})();
