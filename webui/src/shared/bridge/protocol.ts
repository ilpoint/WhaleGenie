/**
 * The bridge protocol's public entry. Domain modules own their shapes; this barrel keeps every
 * existing protocol.js import stable while new code can import the domain file directly.
 */
export * from './protocol/envelope.js';
export * from './protocol/app.js';
export * from './protocol/project.js';
export * from './protocol/settings.js';
export * from './protocol/run.js';
export * from './protocol/library.js';
export * from './protocol/mcp.js';
export * from './protocol/editor.js';
export * from './protocol/editor-value.js';
export * from './protocol/editor-action.js';
export * from './protocol/events.js';
