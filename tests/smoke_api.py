"""End-to-end .NET 8 Minimal API contract smoke test (stdlib only)."""
import base64
import json
import sys
import urllib.error
import urllib.request
import uuid

origin = sys.argv[1].rstrip("/")


def send(path, method="GET", data=None, token=None, kind="application/json", expect=None):
    headers = {}
    if token:
        headers["Authorization"] = "Bearer " + token
    if data is not None:
        if kind == "application/json":
            data = json.dumps(data).encode()
        headers["Content-Type"] = kind
    req = urllib.request.Request(origin + path, method=method, data=data, headers=headers)
    try:
        with urllib.request.urlopen(req) as resp:
            status = resp.status
            content_type = resp.headers.get("content-type", "")
            content = resp.read()
    except urllib.error.HTTPError as ex:
        status = ex.code
        content_type = ex.headers.get("content-type", "")
        content = ex.read()
    if expect is not None and status != expect:
        raise AssertionError(f"{method} {path}: expected {expect}, got {status}: {content[:600]!r}")
    if content_type.startswith("application/json"):
        return status, json.loads(content)
    return status, content


def multipart(fields, filename, content, mimetype):
    boundary = "gamecube" + uuid.uuid4().hex
    body = bytearray()
    for key, value in fields.items():
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"{key}\"\r\n\r\n{value}\r\n").encode()
    body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"{filename}\"\r\n"
             f"Content-Type: {mimetype}\r\n\r\n").encode()
    body += content + f"\r\n--{boundary}--\r\n".encode()
    return bytes(body), "multipart/form-data; boundary=" + boundary


name = uuid.uuid4().hex[:12]
password = "A_long_password!12345"
for email in (f"dev-{name}@example.test", f"reviewer-{name}@example.test"):
    _, registration = send("/account/register", "POST", {"email": email, "password": password}, expect=200)
    # ASP.NET Identity returns HTTP 200 with no JSON body. The portal must
    # handle this successfully before it sends the follow-up login request.
    assert registration == b"", "Identity registration unexpectedly returned a body"

def login(email):
    _, result = send("/account/login?useCookies=false", "POST",
                     {"email": email, "password": password}, expect=200)
    assert result["accessToken"] and result["refreshToken"]
    return result["accessToken"]

developer = login(f"dev-{name}@example.test")
reviewer = login(f"reviewer-{name}@example.test")
_, created = send("/api/developer/software", "POST", {
    "title": "Test Software", "description": "A smoke-test download",
    "category": "Utility"
}, developer, expect=201)
software_id = created["id"]
_, unpublished = send("/api/catalog", expect=200)
assert not any(item["id"] == software_id for item in unpublished)

send(f"/api/developer/software/{software_id}/publish", "POST", token=developer, expect=400)
send(f"/api/developer/software/{software_id}", "PUT",
     {"title": "Tampered", "description": "Wrong owner", "category": "Tool"},
     reviewer, expect=404)

body, content_type = multipart({"version": "1.0.0", "releaseNotes": "First build"},
                               "hello.dol", b"GC executable bytes", "application/octet-stream")
_, asset = send(f"/api/developer/software/{software_id}/releases", "POST",
                body, developer, content_type, expect=201)
assert asset["sha256"] and asset["sizeBytes"] > 0
send(f"/api/developer/software/{software_id}/releases", "POST",
     body, developer, content_type, expect=409)

# A small well-formed PNG (1 x 1) for image header/dimension verification.
png = base64.b64decode(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLytQAAAABJRU5ErkJggg=="
)
body, content_type = multipart({}, "cover.png", png, "image/png")
_, screenshot = send(f"/api/developer/software/{software_id}/screenshots", "POST",
                     body, developer, content_type, expect=201)
_, private_file = send(screenshot["url"], expect=404)
_, own_file = send(screenshot["url"], token=developer, expect=200)
assert own_file.startswith(b"\x89PNG")

send(f"/api/developer/software/{software_id}/publish", "POST",
     token=developer, expect=204)
_, catalog = send("/api/catalog", expect=200)
published = next(item for item in catalog if item["id"] == software_id)
assert published["version"] == "1.0.0"
assert published["coverUrl"] == screenshot["url"]

_, detail = send(f"/api/catalog/{software_id}", expect=200)
assert len(detail["releases"]) == 1 and detail["screenshots"] == [screenshot["url"]]
_, download = send(detail["releases"][0]["downloadUrl"], expect=200)
assert download == b"GC executable bytes"

send(f"/api/catalog/{software_id}/rating", "PUT", {"stars": 5}, reviewer, expect=204)
send(f"/api/catalog/{software_id}/rating", "PUT", {"stars": 4}, reviewer, expect=204)
send(f"/api/catalog/{software_id}/rating", "PUT", {"stars": 5}, developer, expect=400)
_, detail = send(f"/api/catalog/{software_id}", expect=200)
assert detail["ratingCount"] == 1 and detail["averageRating"] == 4

_, mine = send(f"/api/developer/software/{software_id}", token=developer, expect=200)
assert len(mine["screenshotFiles"]) == 1

send(f"/api/developer/software/{software_id}/unpublish", "POST", token=developer, expect=204)
_, hidden = send("/api/catalog", expect=200)
assert not any(item["id"] == software_id for item in hidden)
print("API smoke test passed")
