/**
 * 启停反馈音：Web Audio 合成的两个短音，不引入音频文件。浏览器策略要求 AudioContext 在
 * 用户交互之后创建——启停开关本身就是交互，所以在第一次播放时惰性创建即可。
 */

let context: AudioContext | null = null;

function tone(frequency: number, duration: number): void {
  context ??= new AudioContext();
  if (context.state === 'suspended') {
    void context.resume();
  }

  const oscillator = context.createOscillator();
  const gain = context.createGain();
  oscillator.connect(gain);
  gain.connect(context.destination);
  oscillator.type = 'sine';
  oscillator.frequency.value = frequency;
  gain.gain.setValueAtTime(0.12, context.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.01, context.currentTime + duration);
  oscillator.start();
  oscillator.stop(context.currentTime + duration);
}

/** 启用：一声较高的音。 */
export function playOn(): void {
  tone(880, 0.15);
}

/** 停用：一声较低的音。 */
export function playOff(): void {
  tone(440, 0.15);
}
