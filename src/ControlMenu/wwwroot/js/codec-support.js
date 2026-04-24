window.codecSupport = {
    detect: async function () {
        const codecs = ['h264'];
        const checks = {
            h265: 'hev1.1.6.L93.B0',
            av1: 'av01.0.04M.08'
        };
        for (const [name, webCodec] of Object.entries(checks)) {
            try {
                if (typeof VideoDecoder !== 'undefined' && typeof VideoDecoder.isConfigSupported === 'function') {
                    const result = await VideoDecoder.isConfigSupported({ codec: webCodec });
                    if (result.supported) codecs.push(name);
                }
            } catch { }
        }
        return codecs;
    }
};
