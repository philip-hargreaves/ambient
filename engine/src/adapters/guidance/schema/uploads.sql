-- Ambient added documents: the clinician's own guideline files, sealed. One
-- file, uploads.db, beside the clinical store, with each copied source in
-- files/<id>.bin. Written by the engine only, in WAL mode. application_id
-- 0x414D4255 ("AMBU"), user_version 1 is the format.

-- One row: the embedder every vector was made with, and the store key that
-- wraps each document's key
CREATE TABLE metadata (
    id            INTEGER PRIMARY KEY CHECK (id = 1),
    embedder_id   TEXT    NOT NULL,                 -- model store id, "gte-large-int8"
    embedder_rev  TEXT    NOT NULL,                 -- sha256 of the model weights
    dim           INTEGER NOT NULL CHECK (dim > 0),
    query_prefix  TEXT    NOT NULL DEFAULT '',
    max_tokens    INTEGER NOT NULL,
    created_at    TEXT    NOT NULL,                 -- ISO 8601 UTC
    wrapped_key   BLOB    NOT NULL                  -- store key, DPAPI-protected for the user
);

-- One added document. Plaintext is what the list needs without a key. The
-- name, the passages and the copied file are sealed under the document's key
CREATE TABLE documents (
    id            INTEGER PRIMARY KEY,              -- random, never reused
    key_wrapped   BLOB    NOT NULL,                 -- document key sealed by the store key
    key_seq       INTEGER NOT NULL,                 -- random nonce material for that seal
    name          BLOB    NOT NULL,                 -- sealed display name
    mime          TEXT    NOT NULL,
    bytes         INTEGER NOT NULL CHECK (bytes > 0),
    identity      BLOB    NOT NULL UNIQUE CHECK (length(identity) = 32),  -- keyed hash of the file
    added_at      TEXT    NOT NULL,
    indexed_at    TEXT,                             -- NULL until ready
    state         TEXT    NOT NULL CHECK (state IN ('indexing', 'ready', 'failed', 'stale')),
    error         TEXT,                             -- a reason code, never content
    pages         INTEGER,
    pages_without_text INTEGER
);

-- One passage. ord is dense per document and page is 0-based
CREATE TABLE chunks (
    document_id   INTEGER NOT NULL REFERENCES documents (id) ON DELETE CASCADE,
    ord           INTEGER NOT NULL,
    page          INTEGER NOT NULL,
    section       BLOB    NOT NULL,                 -- sealed heading
    text          BLOB    NOT NULL,                 -- sealed verbatim text
    vec           BLOB    NOT NULL,                 -- sealed f32 unit vector
    boxes         BLOB    NOT NULL,                 -- sealed line boxes as page fractions
    PRIMARY KEY (document_id, ord)
);
