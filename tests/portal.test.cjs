'use strict';

const assert = require('node:assert/strict');
const { readFileSync } = require('node:fs');
const path = require('node:path');
const test = require('node:test');
const vm = require('node:vm');

const source = readFileSync(path.join(__dirname,
  '../src/GameCubeAppStore.Server/wwwroot/portal.js'), 'utf8');

function createPortal(fetchImplementation) {
  const handlers = {};
  const sessions = new Map();
  const page = {innerHTML: '', querySelectorAll: () => []};
  class TestFormData {
    constructor(form) { this.values = form?.values ?? {}; }
    get(name) { return this.values[name] ?? null; }
  }
  const sandbox = vm.createContext({
    Headers, FormData: TestFormData, URL,
    fetch: fetchImplementation,
    sessionStorage: {
      getItem: key => sessions.get(key) ?? null,
      setItem: (key, value) => sessions.set(key, value),
      removeItem: key => sessions.delete(key)
    },
    document: {
      getElementById: id => {
        assert.equal(id, 'app');
        return page;
      },
      addEventListener: (event, handler) => { handlers[event] = handler; }
    }
  });
  vm.runInContext(source, sandbox, {filename: 'portal.js'});
  return {
    page, handlers, sessions, sandbox,
    request: expression => vm.runInContext(expression, sandbox)
  };
}

test('API treats an empty 200 OK response as success instead of parsing JSON', async () => {
  const portal = createPortal(async () => new Response(null, {
    status: 200,
    headers: {'Content-Type': 'application/json'}
  }));
  assert.equal(await portal.request('api("/account/register", {method:"POST",body:"{}"})'), null);
});

test('API handles 204 and whitespace-only success responses', async () => {
  const responses = [
    new Response(null, {status: 204}),
    new Response(' \n ', {status: 200})
  ];
  const portal = createPortal(async () => responses.shift());
  assert.equal(await portal.request('api("/api/developer/software/1", {method:"PUT"})'), null);
  assert.equal(await portal.request('api("/other")'), null);
});

test('API still parses JSON and rejects unsuccessful HTTP responses', async () => {
  const responses = [
    new Response('{"accessToken":"test-token"}', {
      status: 200, headers: {'Content-Type': 'application/json'}
    }),
    new Response('Invalid registration', {status: 400})
  ];
  const portal = createPortal(async () => responses.shift());
  const result = await portal.request('api("/account/login")');
  assert.equal(result.accessToken, 'test-token');
  await assert.rejects(portal.request('api("/account/register")'), /Invalid registration/);
});

test('successful empty-body registration continues to login and displays dashboard', async () => {
  const calls = [];
  const portal = createPortal(async (url, options) => {
    calls.push({url, method: options?.method ?? 'GET'});
    if (url === '/account/register') return new Response(null, {status: 200});
    if (url === '/account/login?useCookies=false') return new Response(JSON.stringify({
      accessToken:'developer-access-token',
      refreshToken:'developer-refresh-token'
    }), {status:200, headers:{'Content-Type':'application/json'}});
    if (url === '/api/developer/software') return new Response('[]', {
      status:200, headers:{'Content-Type':'application/json'}
    });
    throw new Error('Unexpected request: ' + url);
  });
  portal.request('state.authMode="register"');
  const form = {dataset: {form:'auth'}, values:{
    email:'developer@example.test',password:'Strong_password!123'
  }};
  await portal.handlers.submit({
    preventDefault() {},
    target: {closest: selector => selector === 'form[data-form]' ? form : null}
  });
  assert.deepEqual(calls.map(x=>x.url), [
    '/account/register', '/account/login?useCookies=false', '/api/developer/software'
  ]);
  assert.equal(portal.sessions.get('gc_token'), 'developer-access-token');
  assert.match(portal.page.innerHTML, /My software/i);
});
