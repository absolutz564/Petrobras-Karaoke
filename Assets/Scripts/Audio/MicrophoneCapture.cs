using System;
using UnityEngine;

namespace Karaoke.Audio
{
    /// <summary>
    /// Encapsula Microphone.Start() e a leitura das amostras mais recentes.
    /// O AudioClip do microfone e um ring buffer: Microphone.GetPosition() diz
    /// onde esta a "cabeca de gravacao" e nos copiamos a janela imediatamente
    /// anterior a ela, tratando o wrap-around.
    ///
    /// Nao usamos AudioSource para tocar o clip do microfone de proposito:
    /// isso causaria realimentacao (microfonia) nas caixas do jogador.
    /// </summary>
    public class MicrophoneCapture : IDisposable
    {
        public const int PreferredSampleRate = 44100;

        public string Device { get; private set; }
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public string LastError { get; private set; }

        AudioClip clip;
        float[] ring;          // buffer completo do clip (samples * channels)
        int ringSamples;       // amostras por canal

        public bool IsRecording => clip != null && !string.IsNullOrEmpty(Device) && Microphone.IsRecording(Device);

        public static string[] Devices => Microphone.devices;
        public static bool HasDevice => Microphone.devices != null && Microphone.devices.Length > 0;

        /// <summary>Inicia a captura. device null = dispositivo padrao do sistema.</summary>
        public bool Start(string device = null, int lengthSeconds = 1)
        {
            Stop();
            LastError = null;

            if (!HasDevice)
            {
                LastError = "Nenhum microfone encontrado.";
                return false;
            }

            if (string.IsNullOrEmpty(device) || Array.IndexOf(Microphone.devices, device) < 0)
                device = Microphone.devices[0];

            int min, max;
            Microphone.GetDeviceCaps(device, out min, out max);
            int rate = PreferredSampleRate;
            if (max > 0) rate = Mathf.Clamp(rate, Mathf.Max(min, 1), max);
            if (rate <= 0) rate = PreferredSampleRate;

            clip = Microphone.Start(device, true, Mathf.Max(1, lengthSeconds), rate);
            if (clip == null)
            {
                LastError = "Microphone.Start falhou para o dispositivo '" + device + "'.";
                return false;
            }

            Device = device;
            SampleRate = clip.frequency;
            Channels = Mathf.Max(1, clip.channels);
            ringSamples = clip.samples;
            ring = new float[ringSamples * Channels];
            return true;
        }

        public void Stop()
        {
            if (!string.IsNullOrEmpty(Device) && Microphone.IsRecording(Device))
                Microphone.End(Device);
            if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = null;
            ring = null;
            ringSamples = 0;
            Device = null;
        }

        /// <summary>
        /// Copia as ultimas window.Length amostras (mono) do microfone.
        /// Retorna false quando ainda nao ha dados suficientes.
        /// </summary>
        public bool ReadLatest(float[] window)
        {
            if (clip == null || ring == null || window == null) return false;
            int need = window.Length;
            if (need <= 0 || need > ringSamples) return false;

            int pos = Microphone.GetPosition(Device);
            if (pos <= 0) return false;

            // GetData nao faz wrap-around, entao lemos o ring inteiro e
            // resolvemos o wrap em codigo gerenciado (44100 floats/frame e barato).
            if (!clip.GetData(ring, 0)) return false;

            int start = pos - need;
            if (start < 0) start += ringSamples;

            int ch = Channels;
            for (int i = 0; i < need; i++)
            {
                int s = start + i;
                if (s >= ringSamples) s -= ringSamples;
                window[i] = ring[s * ch]; // canal 0
            }
            return true;
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
