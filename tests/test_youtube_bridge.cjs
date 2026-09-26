const {readFileSync} = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const html = readFileSync('Vidi Studio Windows/Assets/youtube.html', 'utf8');
const script = html.match(/<script>([\s\S]*?)<\/script>/)[1];
let state = 1, seek;
const context = {window: {}, URLSearchParams, location: {search: '?id=abcdefghijk', origin: 'https://vidistudio.local'}, YT: {Player: function () {
  return {getCurrentTime: () => 12, getPlayerState: () => state, getDuration: () => 90, getVideoData: () => ({title: 'Video title'}), pauseVideo: () => state = 2, playVideo: () => state = 1, seekTo: t => seek = t};
}}};
vm.createContext(context); vm.runInContext(script, context);
assert.equal(context.window.vidiState().duration, 0);
context.onYouTubeIframeAPIReady();
assert.deepEqual(JSON.parse(JSON.stringify(context.window.vidiState())), {time: 12, playing: true, duration: 90, title: 'Video title'});
context.window.vidiToggle(); assert.equal(context.window.vidiState().playing, false);
context.window.vidiSeek(30); assert.equal(seek, 30);
console.log('PASS: YouTube bridge metadata, uninitialized state, pause, and seek (stubbed player; no network).');
