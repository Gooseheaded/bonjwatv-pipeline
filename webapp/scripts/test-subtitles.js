#!/usr/bin/env node
/**
 * Quick-and-dirty Node.js tests for parseSrt() and toSeconds()
 */
const assert = require('assert');
const path = require('path');
const { parseSrt, toSeconds } = require(path.join(__dirname, '..', 'wwwroot', 'js', 'subtitles.js'));

// toSeconds tests
assert.strictEqual(toSeconds('00:00:00,000'), 0);
assert.strictEqual(toSeconds('00:00:01,000'), 1);
assert.strictEqual(toSeconds('00:01:01,500'), 61.5);
assert.strictEqual(toSeconds('01:00:00,000'), 3600);

// parseSrt tests
const sample = `1
00:00:01,000 --> 00:00:04,000
Hello world

2
00:00:05,500 --> 00:00:06,000
Bye`;
const cues = parseSrt(sample);
assert.strictEqual(cues.length, 2);
assert.strictEqual(cues[0].start, 1);
assert.strictEqual(cues[0].end, 4);
assert.strictEqual(cues[0].text, 'Hello world');
assert.strictEqual(cues[1].start, 5.5);
assert.strictEqual(cues[1].end, 6);
assert.strictEqual(cues[1].text, 'Bye');

console.log('✔ subtitle parser tests passed');

// Remote HTTP(S) SRT fetch & parse test
;(async () => {
    const url = 'https://pastebin.com/raw/HBEqmvNP';
    console.log('Testing remote SRT fetch from', url);
    const res = await fetch(url);
    assert(res.ok, `Failed to fetch remote SRT: ${res.status}`);
    const text = await res.text();
    const cues2 = parseSrt(text);
    assert(cues2.length > 20, `Expected >20 cues, got ${cues2.length}`);
    assert.strictEqual(cues2[0].text, 'Um...', 'First remote cue text mismatch');
    assert.strictEqual(cues2[1].start, 10, 'Second remote cue start mismatch');
    console.log('✔ remote subtitle fetch & parse tests passed');
})().catch(err => {
    console.error('Remote SRT fetch test failed:', err);
    process.exit(1);
});