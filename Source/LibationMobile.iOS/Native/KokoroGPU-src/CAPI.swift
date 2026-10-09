import Foundation
import MLX

// A C interface for apps that are not written in Swift (Libation's .NET app): open the model, speak a text in a voice
// given as its raw style file (Kokoro's voices/NAME.bin: 510 x 256 float32), and free what was returned.

private final class Engine {
  let tts: KokoroTTS
  var voices: [String: MLXArray] = [:]
  init(tts: KokoroTTS) { self.tts = tts }
}

/// Opens the model (kokoro-v1_0.safetensors). Returns a handle, or nil if it could not be loaded.
@_cdecl("kokoro_open")
public func kokoro_open(_ modelPath: UnsafePointer<CChar>) -> UnsafeMutableRawPointer? {
  let url = URL(fileURLWithPath: String(cString: modelPath))
  guard FileManager.default.fileExists(atPath: url.path) else { return nil }
  let engine = Engine(tts: KokoroTTS(modelPath: url))
  return Unmanaged.passRetained(engine).toOpaque()
}

/// Speaks `text` in the voice whose style file is `voicePath`; British English if `british` is non-zero. Writes the
/// 24 kHz mono samples' address and count; free them with kokoro_free. Returns 0, or -1 on failure.
@_cdecl("kokoro_speak")
public func kokoro_speak(_ handle: UnsafeMutableRawPointer, _ voicePath: UnsafePointer<CChar>, _ text: UnsafePointer<CChar>,
                         _ british: Int32, _ speed: Float, _ samples: UnsafeMutablePointer<UnsafeMutablePointer<Float>?>,
                         _ count: UnsafeMutablePointer<Int32>) -> Int32 {
  let engine = Unmanaged<Engine>.fromOpaque(handle).takeUnretainedValue()
  let path = String(cString: voicePath)
  let voice: MLXArray
  if let known = engine.voices[path] {
    voice = known
  } else {
    guard let data = FileManager.default.contents(atPath: path), data.count == 510 * 256 * 4 else { return -1 }
    let floats = data.withUnsafeBytes { Array($0.bindMemory(to: Float.self)) }
    voice = MLXArray(floats, [510, 1, 256])
    engine.voices[path] = voice
  }
  do {
    let (audio, _) = try engine.tts.generateAudio(voice: voice, language: british != 0 ? .enGB : .enUS, text: String(cString: text), speed: speed)
    let buffer = UnsafeMutablePointer<Float>.allocate(capacity: max(1, audio.count))
    audio.withUnsafeBufferPointer { buffer.initialize(from: $0.baseAddress!, count: audio.count) }
    samples.pointee = buffer
    count.pointee = Int32(audio.count)
    return 0
  } catch {
    return -1
  }
}

@_cdecl("kokoro_free")
public func kokoro_free(_ samples: UnsafeMutablePointer<Float>?) {
  samples?.deallocate()
}

@_cdecl("kokoro_close")
public func kokoro_close(_ handle: UnsafeMutableRawPointer) {
  Unmanaged<Engine>.fromOpaque(handle).release()
}
