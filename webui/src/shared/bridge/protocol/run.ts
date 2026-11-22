/** run bridge shapes. The barrel protocol.ts re-exports this file. */

export interface RunState {
  loaded: string;
  status: string;
  statusKind: string;
  busy: boolean;
  paused: boolean;
  canRun: boolean;
  canStep: boolean;
  canStop: boolean;
  question: string;
  wantsTypedAnswer: boolean;
  answerText: string;
  isAskingUser: boolean;
  isAsking: boolean;
  arguments: RunArgument[];
  look: RunLook | null;
  globals: RunGlobal[];
  locals: RunLocal[];
  steps: RunStep[];
  log: RunLine[];
}

export interface RunLook {
  stepId: string;
  type: string;
  looking: string;
  image: string;
}

export interface RunArgument {
  name: string;
  label: string;
  hint: string;
  required: boolean;
  value: string;
  given: boolean;
  missing: boolean;
}

export interface RunGlobal {
  name: string;
  type: string;
  value: string;
  defaultValue: string;
}

export interface RunLocal {
  name: string;
  macro: string;
  type: string;
}

export interface RunStep {
  id: string;
  label: string;
  detail: string;
  depth: number;
  shown: boolean;
  container: boolean;
  current: boolean;
  breakpoint: boolean;
  canBreak: boolean;
  time: string;
  failure: string;
  condition: string;
  hitTimes: number;
  expression: string;
}

export interface RunLine {
  time: string;
  level: string;
  text: string;
}
