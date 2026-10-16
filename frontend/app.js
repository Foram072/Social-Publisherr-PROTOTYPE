// When the page is opened from the API itself (http://localhost:5293) the addresses are relative.
// When it is opened as a file from disk (file://), it talks to the API on localhost:5293.
const API_ORIGIN = window.location.protocol === "file:" ? "http://localhost:5293" : "";
const API_BASE = `${API_ORIGIN}/api/social`;

const PLATFORM_ICONS = {
    facebook: "bi-facebook",
    instagram: "bi-instagram",
    linkedin: "bi-linkedin"
};

const PLATFORM_COLORS = {
    facebook: "#1877f2",
    instagram: "#e1306c",
    linkedin: "#0a66c2"
};

// ============================================================
// SUPPORTED FILE TYPES
// ============================================================

const IMAGE_EXTENSIONS = [".jpg", ".jpeg", ".png"];
const VIDEO_EXTENSIONS = [".mp4", ".mov"];

// Documents (LinkedIn only): label + icon + colour for the preview card
const DOCUMENT_TYPES = {
    ".pdf": { label: "PDF", icon: "bi-file-earmark-pdf-fill", color: "#dc3545" },
    ".doc": { label: "Word", icon: "bi-file-earmark-word-fill", color: "#2b579a" },
    ".docx": { label: "Word", icon: "bi-file-earmark-word-fill", color: "#2b579a" },
    ".ppt": { label: "PowerPoint", icon: "bi-file-earmark-ppt-fill", color: "#d24726" },
    ".pptx": { label: "PowerPoint", icon: "bi-file-earmark-ppt-fill", color: "#d24726" },
    ".xls": { label: "Excel", icon: "bi-file-earmark-excel-fill", color: "#198754" },
    ".xlsx": { label: "Excel", icon: "bi-file-earmark-excel-fill", color: "#198754" },
    ".csv": { label: "CSV", icon: "bi-file-earmark-spreadsheet-fill", color: "#198754" }
};

const SUPPORTED_TYPES_TEXT =
    "JPG, PNG, MP4, MOV (images/videos) or PDF, DOC, DOCX, PPT, PPTX, XLS, XLSX, CSV (LinkedIn documents)";

function getExtension(fileName) {

    const name = String(fileName || "").toLowerCase();
    const dot = name.lastIndexOf(".");

    return dot >= 0 ? name.substring(dot) : "";
}

// Returns "image", "video", "document" or "unknown"
function getFileKind(file) {

    const ext = getExtension(file.name);

    if (IMAGE_EXTENSIONS.includes(ext)) return "image";
    if (VIDEO_EXTENSIONS.includes(ext)) return "video";
    if (DOCUMENT_TYPES[ext]) return "document";

    // Fall back to the browser's MIME type when the extension is missing
    if (file.type === "image/jpeg" || file.type === "image/png") return "image";
    if (file.type === "video/mp4" || file.type === "video/quicktime") return "video";
    if (file.type === "application/pdf") return "document";

    return "unknown";
}

function getDocumentInfo(file) {

    const ext = getExtension(file.name);

    if (DOCUMENT_TYPES[ext]) {
        return DOCUMENT_TYPES[ext];
    }

    if (file.type === "application/pdf") {
        return DOCUMENT_TYPES[".pdf"];
    }

    return {
        label: "Document",
        icon: "bi-file-earmark-fill",
        color: "#6c757d"
    };
}


// ============================================================
// GLOBAL MEDIA STATE
// ============================================================

// IMPORTANT:
// This must be outside publishPost() because multiple functions
// need access to the selected files.
let selectedMediaFiles = [];


// ============================================================
// STARTUP
// ============================================================

document.addEventListener("DOMContentLoaded", () => {

    loadAccounts();

    const mediaInput = document.getElementById("postMedia");

    if (mediaInput) {
        mediaInput.addEventListener("change", handleMediaSelection);
    }
});


// ============================================================
// MEDIA SELECTION
// ============================================================

function handleMediaSelection() {

    const input = document.getElementById("postMedia");

    if (!input || !input.files) {
        return;
    }

    const newFiles = Array.from(input.files);

    // Add newly selected files to the existing list.
    selectedMediaFiles.push(...newFiles);

    // Optional: prevent duplicate files
    selectedMediaFiles = selectedMediaFiles.filter(
        (file, index, array) =>
            index === array.findIndex(
                f =>
                    f.name === file.name &&
                    f.size === file.size &&
                    f.lastModified === file.lastModified
            )
    );

    syncMediaInput();
    renderMediaPreviews();
}


// ============================================================
// KEEP INPUT IN SYNC WITH OUR ARRAY
// ============================================================

function syncMediaInput() {

    const input = document.getElementById("postMedia");

    if (!input) {
        return;
    }

    const dataTransfer = new DataTransfer();

    selectedMediaFiles.forEach(file => {
        dataTransfer.items.add(file);
    });

    input.files = dataTransfer.files;
}


// ============================================================
// REMOVE ONE MEDIA FILE
// ============================================================

function removeMedia(index) {

    if (index < 0 || index >= selectedMediaFiles.length) {
        return;
    }

    selectedMediaFiles.splice(index, 1);

    syncMediaInput();
    renderMediaPreviews();
}


// ============================================================
// REMOVE ALL MEDIA
// ============================================================

function clearMedia() {

    selectedMediaFiles = [];

    const input = document.getElementById("postMedia");

    if (input) {
        input.value = "";
    }

    renderMediaPreviews();
}


// ============================================================
// MEDIA PREVIEW
// ============================================================

function renderMediaPreviews() {

    const wrapper = document.getElementById("mediaPreviewWrapper");
    const list = document.getElementById("mediaPreviewList");

    if (!wrapper || !list) {
        return;
    }

    if (selectedMediaFiles.length === 0) {

        wrapper.classList.add("d-none");
        list.innerHTML = "";

        return;
    }

    wrapper.classList.remove("d-none");

    list.innerHTML = "";

    selectedMediaFiles.forEach((file, index) => {

        const kind = getFileKind(file);

        let previewHtml;

        if (kind === "video") {

            const url = URL.createObjectURL(file);

            previewHtml = `
                <video
                    src="${url}"
                    controls
                    preload="metadata"
                    style="
                        width:100%;
                        height:150px;
                        object-fit:cover;
                        border-radius:8px;
                    ">
                </video>
            `;

        } else if (kind === "document") {

            const info = getDocumentInfo(file);

            previewHtml = `
                <div style="
                    width:100%;
                    height:150px;
                    border-radius:8px;
                    background:#f8f9fa;
                    display:flex;
                    flex-direction:column;
                    align-items:center;
                    justify-content:center;
                    border: 2px dashed #dee2e6;
                ">
                    <i class="bi ${info.icon}" style="font-size:3rem; color:${info.color};"></i>
                    <span class="badge mt-2" style="background:${info.color};">${info.label}</span>
                    <small class="text-muted mt-1">LinkedIn only</small>
                </div>
            `;

        } else if (kind === "image") {

            const url = URL.createObjectURL(file);

            previewHtml = `
                <img
                    src="${url}"
                    alt="${escapeHtml(file.name)}"
                    style="
                        width:100%;
                        height:150px;
                        object-fit:cover;
                        border-radius:8px;
                    ">
            `;

        } else {

            // Unknown type: show a neutral card instead of a broken image
            previewHtml = `
                <div style="
                    width:100%;
                    height:150px;
                    border-radius:8px;
                    background:#f8f9fa;
                    display:flex;
                    flex-direction:column;
                    align-items:center;
                    justify-content:center;
                    border: 2px dashed #dc3545;
                ">
                    <i class="bi bi-exclamation-triangle-fill" style="font-size:2.5rem; color:#dc3545;"></i>
                    <small class="text-danger mt-2">Unsupported file</small>
                </div>
            `;
        }

        const item = document.createElement("div");

        item.className = "col-6 col-md-4 col-lg-3";

        item.innerHTML = `
            <div class="border rounded p-2 bg-white h-100 position-relative shadow-sm">

                ${previewHtml}

                <div class="small text-truncate mt-2 fw-semibold"
                     title="${escapeHtml(file.name)}">
                    ${index + 1}. ${escapeHtml(file.name)}
                </div>

                <div class="small text-muted">
                    ${formatFileSize(file.size)}
                </div>

                <button
                    type="button"
                    class="btn btn-sm btn-danger position-absolute top-0 end-0 m-1"
                    onclick="removeMedia(${index})"
                    title="Remove this file">

                    <i class="bi bi-x-lg"></i>

                </button>

            </div>
        `;

        list.appendChild(item);
    });
}


// ============================================================
// FILE VALIDATION
// ============================================================

function validateMediaFiles() {

    for (const file of selectedMediaFiles) {

        const kind = getFileKind(file);

        if (kind === "unknown") {

            return {
                valid: false,
                message:
                    `Unsupported file type: ${file.name}. ` +
                    `Use ${SUPPORTED_TYPES_TEXT}.`
            };
        }

        // Image maximum = 8 MB
        if (kind === "image" && file.size > 8 * 1024 * 1024) {

            return {
                valid: false,
                message:
                    `Image "${file.name}" is too large. ` +
                    `Maximum image size is 8 MB.`
            };
        }

        // Video maximum = 100 MB
        if (kind === "video" && file.size > 100 * 1024 * 1024) {

            return {
                valid: false,
                message:
                    `Video "${file.name}" is too large. ` +
                    `Maximum video size is 100 MB.`
            };
        }

        // Document maximum = 50 MB
        if (kind === "document" && file.size > 50 * 1024 * 1024) {

            return {
                valid: false,
                message:
                    `Document "${file.name}" is too large. ` +
                    `Maximum document size is 50 MB.`
            };
        }
    }

    return {
        valid: true,
        message: ""
    };
}


// ============================================================
// PLATFORM-SPECIFIC VALIDATION
// ============================================================

function validatePlatformMedia(selectedPlatforms) {

    const images = selectedMediaFiles.filter(f => getFileKind(f) === "image");
    const videos = selectedMediaFiles.filter(f => getFileKind(f) === "video");
    const documents = selectedMediaFiles.filter(f => getFileKind(f) === "document");

    const hasMedia = selectedMediaFiles.length > 0;

    // --------------------------------------------------------
    // Documents can only be posted to LinkedIn
    // --------------------------------------------------------

    if (documents.length > 0) {

        const others = selectedPlatforms.filter(p => p !== "linkedin");

        if (others.length > 0) {

            return {
                valid: false,
                message:
                    "Documents (PDF, Word, PowerPoint, Excel, CSV) can only be posted to LinkedIn. " +
                    "Untick the other platforms, or remove the document."
            };
        }
    }

    // --------------------------------------------------------
    // Instagram
    // --------------------------------------------------------

    if (selectedPlatforms.includes("instagram")) {

        if (!hasMedia || (images.length + videos.length) === 0) {

            return {
                valid: false,
                message:
                    "Instagram requires at least one image or video."
            };
        }

        // Instagram carousel supports up to 10 items.
        if (selectedMediaFiles.length > 10) {

            return {
                valid: false,
                message:
                    "Instagram supports a maximum of 10 media items in one carousel."
            };
        }
    }

    // --------------------------------------------------------
    // LinkedIn
    // --------------------------------------------------------

    if (selectedPlatforms.includes("linkedin")) {

        if (documents.length > 1) {

            return {
                valid: false,
                message:
                    "LinkedIn allows only one document per post. Remove the extra documents."
            };
        }

        if (documents.length === 1 && (images.length > 0 || videos.length > 0)) {

            return {
                valid: false,
                message:
                    "LinkedIn cannot combine a document with images or a video. Post the document on its own."
            };
        }

        if (videos.length > 1) {

            return {
                valid: false,
                message:
                    "LinkedIn allows only one video per post. Remove the extra videos."
            };
        }

        if (videos.length === 1 && images.length > 0) {

            return {
                valid: false,
                message:
                    "LinkedIn cannot combine images and a video in one post. Use only images, or a single video."
            };
        }

        if (images.length > 9) {

            return {
                valid: false,
                message:
                    "LinkedIn allows up to 9 images in one post."
            };
        }
    }

    return {
        valid: true,
        message: ""
    };
}


// ============================================================
// PUBLISH POST
// ============================================================

async function publishPost() {

    const content =
        document.getElementById("postContent")
            .value
            .trim();

    const title =
        document.getElementById("postTitle")
            .value
            .trim();

    const btn =
        document.getElementById("publishBtn");


    // --------------------------------------------------------
    // Content validation
    // --------------------------------------------------------

    if (!content) {

        document
            .getElementById("postContent")
            .classList.add("is-invalid");

        alert("Post content is required.");

        return;
    }

    document
        .getElementById("postContent")
        .classList.remove("is-invalid");


    // --------------------------------------------------------
    // Platform validation
    // --------------------------------------------------------

    const selected =
        Array.from(
            document.querySelectorAll(
                ".platform-checkbox:checked"
            )
        ).map(cb => cb.value);

    if (selected.length === 0) {

        alert(
            "Please select at least one social media platform."
        );

        return;
    }


    // --------------------------------------------------------
    // Media validation
    // --------------------------------------------------------

    const mediaValidation =
        validateMediaFiles();

    if (!mediaValidation.valid) {

        alert(mediaValidation.message);

        return;
    }


    const platformValidation =
        validatePlatformMedia(selected);

    if (!platformValidation.valid) {

        alert(platformValidation.message);

        return;
    }


    // --------------------------------------------------------
    // Prevent duplicate submission
    // --------------------------------------------------------

    btn.disabled = true;

    btn.innerHTML = `
        <span class="spinner-border spinner-border-sm me-2"></span>
        Uploading media...
    `;


    try {

        // ====================================================
        // STEP 1: Upload every selected media file
        // ====================================================

        const mediaIds = [];

        for (
            let index = 0;
            index < selectedMediaFiles.length;
            index++
        ) {

            const file =
                selectedMediaFiles[index];

            btn.innerHTML = `
                <span class="spinner-border spinner-border-sm me-2"></span>
                Uploading ${index + 1}/${selectedMediaFiles.length}...
            `;


            const formData =
                new FormData();

            formData.append(
                "file",
                file
            );


            const uploadResponse =
                await fetch(
                    `${API_BASE}/media`,
                    {
                        method: "POST",
                        body: formData
                    }
                );


            let uploadResult;

            try {

                uploadResult =
                    await uploadResponse.json();

            } catch {

                uploadResult = {};
            }


            if (!uploadResponse.ok) {

                throw new Error(
                    `Upload failed for "${file.name}": ` +
                    `${uploadResult.error || "Unknown error"}`
                );
            }


            if (
                uploadResult.id === undefined ||
                uploadResult.id === null
            ) {

                throw new Error(
                    `Upload succeeded but no media ID was returned for "${file.name}".`
                );
            }


            mediaIds.push(
                uploadResult.id
            );
        }


        // ====================================================
        // STEP 2: Create/publish post
        // ====================================================

        btn.innerHTML = `
            <span class="spinner-border spinner-border-sm me-2"></span>
            Publishing...
        `;


        const payload = {

            title: title,

            content: content,

            platforms: selected,

            // IMPORTANT:
            // This now contains ALL uploaded media IDs.
            mediaIds: mediaIds
        };


        console.log(
            "Publish payload:",
            payload
        );


        const res =
            await fetch(
                `${API_BASE}/posts`,
                {
                    method: "POST",

                    headers: {
                        "Content-Type":
                            "application/json"
                    },

                    body:
                        JSON.stringify(payload)
                }
            );


        let result;

        try {

            result =
                await res.json();

        } catch {

            result = {};
        }


        if (!res.ok) {

            throw new Error(
                result.error ||
                result.message ||
                "Post publishing failed."
            );
        }


        // ====================================================
        // STEP 3: Display result
        // ====================================================

        document
            .getElementById("resultSection")
            .classList.remove("d-none");


        renderResults(
            result.postId,
            result.status,
            result.results || []
        );


        document
            .getElementById("resultSection")
            .scrollIntoView({
                behavior: "smooth"
            });


    } catch (err) {

        console.error(
            "Publish error:",
            err
        );

        alert(
            err.message ||
            "Failed to publish the post. Check the backend API."
        );

    } finally {

        btn.disabled = false;

        btn.innerHTML = `
            <i class="bi bi-send-fill"></i>
            Publish Now
        `;
    }
}


// ============================================================
// LOAD SOCIAL ACCOUNTS
// ============================================================

async function loadAccounts() {

    const container =
        document.getElementById(
            "platformsContainer"
        );

    try {

        const res =
            await fetch(
                `${API_BASE}/accounts`
            );


        if (!res.ok) {

            throw new Error(
                "API returned " +
                res.status
            );
        }


        const accounts =
            await res.json();


        if (!accounts || accounts.length === 0) {

            container.innerHTML = `
                <div class="alert alert-warning mb-0">

                    <i class="bi bi-exclamation-triangle"></i>

                    No social accounts configured.

                    Add credentials to
                    <code>appsettings.json</code>
                    and restart the API.

                </div>
            `;

            return;
        }


        let html = "";


        accounts.forEach(acc => {

            const icon =
                PLATFORM_ICONS[acc.platform]
                || "bi-globe";

            const color =
                PLATFORM_COLORS[acc.platform]
                || "#888";

            const isConnected =
                acc.isConnected;


            html += `

                <div
                    class="platform-row d-flex
                           align-items-center
                           justify-content-between
                           p-3 border-bottom">

                    <div
                        class="d-flex
                               align-items-center
                               gap-3">

                        <input
                            class="form-check-input
                                   platform-checkbox mt-0"
                            type="checkbox"
                            value="${acc.platform}"
                            id="chk_${acc.platform}"
                            ${isConnected
                    ? ""
                    : "disabled"}>

                        <label
                            class="form-check-label
                                   d-flex
                                   align-items-center
                                   gap-2
                                   mb-0
                                   fw-semibold"
                            for="chk_${acc.platform}">

                            <i
                                class="bi ${icon} fs-5"
                                style="color:${color}">
                            </i>

                            <span
                                class="text-capitalize">
                                ${acc.platform}
                            </span>

                        </label>

                    </div>


                    <div
                        class="d-flex
                               align-items-center
                               gap-2">

                        <span
                            class="badge ${isConnected
                    ? "bg-success"
                    : "bg-danger"
                }">

                            <i
                                class="bi ${isConnected
                    ? "bi-check-circle"
                    : "bi-x-circle"
                }">
                            </i>

                            ${isConnected
                    ? "Connected"
                    : "Not Connected"
                }

                        </span>


                        ${!isConnected
                    ? `
                                    <button
                                        class="btn btn-sm
                                               btn-outline-primary"
                                        onclick="connectPlatform('${acc.platform}')">

                                        <i
                                            class="bi bi-link-45deg">
                                        </i>

                                        Connect

                                    </button>
                                  `
                    : ""
                }

                    </div>

                </div>
            `;
        });


        container.innerHTML = html;


    } catch (err) {

        container.innerHTML = `

            <div class="alert alert-danger mb-0">

                <i class="bi bi-exclamation-circle"></i>

                <strong>
                    Cannot reach backend API.
                </strong>

                <br>

                Make sure the API is running at

                <code>${API_BASE}</code>

            </div>
        `;

        console.error(err);
    }
}


// ============================================================
// OAUTH CONNECTION
// ============================================================

function connectPlatform(platform) {

    const oauthUrl =
        `${API_BASE}/accounts/connect/${platform}`;


    const popup =
        window.open(
            oauthUrl,
            `Connect ${platform}`,
            "width=600,height=700,scrollbars=yes,resizable=yes"
        );


    const timer =
        setInterval(() => {

            if (
                popup &&
                popup.closed
            ) {

                clearInterval(timer);

                loadAccounts();
            }

        }, 1000);
}


// ============================================================
// RENDER PUBLISHING RESULTS
// ============================================================

function renderResults(
    postId,
    overallStatus,
    results
) {

    const container =
        document.getElementById(
            "resultsContainer"
        );


    const statusBadge =
        overallStatus === "Published"

            ? `<span class="badge bg-success">
                   Published
               </span>`

            : overallStatus === "PartiallyPublished"

                ? `<span class="badge bg-warning text-dark">
                       Partially Published
                   </span>`

                : `<span class="badge bg-danger">
                       Failed
                   </span>`;


    let html = `

        <div
            class="p-3 border-bottom
                   bg-light
                   d-flex
                   justify-content-between
                   align-items-center">

            <span class="fw-semibold">
                Post #${postId} – Overall Status
            </span>

            ${statusBadge}

        </div>

        <ul class="list-group list-group-flush">
    `;


    if (!results || results.length === 0) {

        html += `

            <li class="list-group-item p-4">

                <div class="alert alert-warning mb-0">

                    Post was created, but no platform
                    result was returned.

                </div>

            </li>
        `;

    } else {

        results.forEach(r => {

            const icon =
                PLATFORM_ICONS[r.platform]
                || "bi-globe";

            const color =
                PLATFORM_COLORS[r.platform]
                || "#888";

            const isPublished =
                r.status === "Published";


            let actions = "";


            if (
                isPublished &&
                r.postUrl
            ) {

                actions += `

                    <a
                        href="${r.postUrl}"
                        target="_blank"
                        rel="noopener noreferrer"
                        class="btn btn-sm
                               btn-outline-primary
                               ms-2">

                        <i
                            class="bi bi-box-arrow-up-right">
                        </i>

                        View Post

                    </a>
                `;
            }


            if (!isPublished) {

                actions += `

                    <button
                        class="btn btn-sm
                               btn-warning ms-2"
                        onclick="retryPost(${postId})">

                        <i
                            class="bi bi-arrow-clockwise">
                        </i>

                        Retry

                    </button>
                `;
            }


            html += `

                <li
                    class="list-group-item
                           d-flex
                           justify-content-between
                           align-items-center
                           px-4 py-3">

                    <div
                        class="d-flex
                               align-items-center
                               gap-3">

                        <i
                            class="bi ${icon} fs-4"
                            style="color:${color}">
                        </i>


                        <div>

                            <div
                                class="fw-semibold
                                       text-capitalize">

                                ${r.platform}

                            </div>


                            <div
                                class="small ${isPublished
                    ? "text-success"
                    : "text-danger"
                }">

                                <i
                                    class="bi ${isPublished
                    ? "bi-check-circle-fill"
                    : "bi-x-circle-fill"
                }">
                                </i>

                                ${isPublished
                    ? "Post published successfully"
                    : (
                        r.error ||
                        "Failed"
                    )
                }

                            </div>

                        </div>

                    </div>


                    <div
                        class="d-flex
                               align-items-center">

                        <span
                            class="badge ${isPublished
                    ? "bg-success"
                    : "bg-danger"
                }">

                            ${r.status}

                        </span>

                        ${actions}

                    </div>

                </li>
            `;
        });
    }


    html += "</ul>";

    container.innerHTML = html;
}


// ============================================================
// RETRY FAILED PLATFORMS
// ============================================================

async function retryPost(postId) {

    const btn =
        event.target.closest("button");

    if (btn) {

        btn.disabled = true;

        btn.innerHTML = `
            <span
                class="spinner-border
                       spinner-border-sm">
            </span>
        `;
    }


    try {

        const res =
            await fetch(
                `${API_BASE}/posts/${postId}/retry`,
                {
                    method: "POST"
                }
            );


        const result =
            await res.json();


        if (!res.ok) {

            throw new Error(
                result.error ||
                "Retry failed."
            );
        }


        renderResults(
            result.postId,
            result.status,
            result.results || []
        );


    } catch (err) {

        alert(
            err.message ||
            "Retry failed. Check the API."
        );

        console.error(err);


    } finally {

        if (btn) {

            btn.disabled = false;

            btn.innerHTML = `
                <i class="bi bi-arrow-clockwise"></i>
                Retry
            `;
        }
    }
}


// ============================================================
// PUBLISHING HISTORY
// ============================================================

// How many posts are shown on one page of the history window
const HISTORY_PAGE_SIZE = 10;

let historyPosts = [];
let historyPage = 1;

async function showHistory(keepPage) {

    const modal =
        bootstrap.Modal.getOrCreateInstance(
            document.getElementById(
                "historyModal"
            )
        );


    const container =
        document.getElementById(
            "historyContainer"
        );


    container.innerHTML = `

        <div class="text-center py-3">

            <div class="spinner-border"></div>

        </div>
    `;


    modal.show();


    try {

        const res =
            await fetch(
                `${API_BASE}/posts`
            );


        if (!res.ok) {

            throw new Error(
                "Failed to load history."
            );
        }


        const posts =
            await res.json();


        // Newest post first
        historyPosts =
            (posts || [])
                .slice()
                .sort((a, b) => (b.id || 0) - (a.id || 0));


        // After a retry we stay on the same page, otherwise we start on page 1
        if (!keepPage) {

            historyPage = 1;
        }


        renderHistoryPage(historyPage);


    } catch (err) {

        container.innerHTML = `

            <div class="alert alert-danger">

                Failed to load history.

            </div>
        `;

        console.error(err);
    }
}


// ============================================================
// HISTORY: SHOW ONE PAGE
// ============================================================

function renderHistoryPage(page) {

    const container =
        document.getElementById(
            "historyContainer"
        );


    if (!historyPosts || historyPosts.length === 0) {

        container.innerHTML = `

            <p class="text-muted">
                No posts published yet.
            </p>
        `;

        return;
    }


    const totalPages =
        Math.max(
            1,
            Math.ceil(
                historyPosts.length / HISTORY_PAGE_SIZE
            )
        );

    historyPage =
        Math.min(
            Math.max(1, page),
            totalPages
        );


    const start =
        (historyPage - 1) * HISTORY_PAGE_SIZE;

    const pagePosts =
        historyPosts.slice(
            start,
            start + HISTORY_PAGE_SIZE
        );


    let html = `

        <div class="table-responsive">

            <table
                class="table
                       table-hover
                       align-middle">

                <thead class="table-dark">

                    <tr>

                        <th>#</th>
                        <th>Title</th>
                        <th>Status</th>
                        <th>Platforms</th>
                        <th>Date</th>
                        <th>Actions</th>

                    </tr>

                </thead>

                <tbody>
    `;


    pagePosts.forEach(p => {

        const date =
            new Date(
                p.createdAt
            ).toLocaleString();


        const platformBadges =
            (p.results || [])
                .map(r => {

                    const cls =
                        r.status === "Published"
                            ? "bg-success"
                            : "bg-danger";


                    return `

                        <span
                            class="badge
                                   ${cls}
                                   text-capitalize
                                   me-1">

                            ${r.platform}

                        </span>
                    `;
                })
                .join("");


        const hasFailure =
            (p.results || [])
                .some(
                    r =>
                        r.status !==
                        "Published"
                );


        const retryBtn =
            hasFailure

                ? `

                    <button
                        class="btn btn-sm
                               btn-warning"
                        onclick="retryFromHistory(
                            ${p.id},
                            this
                        )">

                        <i
                            class="bi bi-arrow-clockwise">
                        </i>

                        Retry Failed

                    </button>
                  `

                : "";


        const statusClass =
            p.status === "Published"

                ? "bg-success"

                : p.status === "PartiallyPublished"

                    ? "bg-warning text-dark"

                    : "bg-danger";


        html += `

            <tr>

                <td>
                    ${p.id}
                </td>

                <td>
                    ${p.title
                ? escapeHtml(p.title)
                : "<em class='text-muted'>No title</em>"
            }
                </td>

                <td>

                    <span
                        class="badge ${statusClass}">

                        ${p.status}

                    </span>

                </td>

                <td>
                    ${platformBadges}
                </td>

                <td
                    class="text-muted small">

                    ${date}

                </td>

                <td>
                    ${retryBtn}
                </td>

            </tr>
        `;
    });


    html += `

                </tbody>

            </table>

        </div>
    `;


    html += buildHistoryPagination(
        totalPages,
        start,
        pagePosts.length
    );


    container.innerHTML = html;
}


// ============================================================
// HISTORY: PAGE BUTTONS  (Prev  1 2 3 ... 12  Next)
// ============================================================

function buildHistoryPagination(totalPages, start, countOnPage) {

    const total = historyPosts.length;

    const from = start + 1;
    const to = start + countOnPage;

    const summary = `
        <div class="text-muted small">
            Showing ${from}-${to} of ${total} posts
        </div>
    `;


    // Nothing to page through
    if (totalPages <= 1) {

        return `
            <div class="mt-3">
                ${summary}
            </div>
        `;
    }


    // Page numbers: first, last, and the pages around the current one
    const numbers = [];

    for (let i = 1; i <= totalPages; i++) {

        if (
            i === 1 ||
            i === totalPages ||
            Math.abs(i - historyPage) <= 1
        ) {

            numbers.push(i);

        } else if (numbers[numbers.length - 1] !== "...") {

            numbers.push("...");
        }
    }


    const items = numbers.map(n => {

        if (n === "...") {

            return `
                <li class="page-item disabled">
                    <span class="page-link">...</span>
                </li>
            `;
        }

        return `
            <li class="page-item ${n === historyPage ? "active" : ""}">
                <button
                    type="button"
                    class="page-link"
                    onclick="renderHistoryPage(${n})">
                    ${n}
                </button>
            </li>
        `;

    }).join("");


    return `

        <div
            class="d-flex
                   justify-content-between
                   align-items-center
                   flex-wrap
                   gap-2
                   mt-3">

            ${summary}

            <nav aria-label="History pages">

                <ul class="pagination pagination-sm mb-0">

                    <li class="page-item ${historyPage === 1 ? "disabled" : ""}">
                        <button
                            type="button"
                            class="page-link"
                            onclick="renderHistoryPage(${historyPage - 1})">
                            &laquo; Prev
                        </button>
                    </li>

                    ${items}

                    <li class="page-item ${historyPage === totalPages ? "disabled" : ""}">
                        <button
                            type="button"
                            class="page-link"
                            onclick="renderHistoryPage(${historyPage + 1})">
                            Next &raquo;
                        </button>
                    </li>

                </ul>

            </nav>

        </div>
    `;
}


// ============================================================
// RETRY FROM HISTORY
// ============================================================

async function retryFromHistory(
    postId,
    btn
) {

    btn.disabled = true;

    btn.innerHTML = `
        <span
            class="spinner-border
                   spinner-border-sm">
        </span>
    `;


    try {

        const res =
            await fetch(
                `${API_BASE}/posts/${postId}/retry`,
                {
                    method: "POST"
                }
            );


        const result =
            await res.json();


        if (!res.ok) {

            throw new Error(
                result.error ||
                "Retry failed."
            );
        }


        alert(
            `Retry complete. Status: ${result.status}`
        );


        showHistory(true);


    } catch (err) {

        alert(
            err.message ||
            "Retry failed."
        );

        console.error(err);


    } finally {

        btn.disabled = false;

        btn.innerHTML = `
            <i class="bi bi-arrow-clockwise"></i>
            Retry Failed
        `;
    }
}


// ============================================================
// HELPER FUNCTIONS
// ============================================================

function formatFileSize(bytes) {

    if (bytes === 0) {
        return "0 Bytes";
    }

    const units = [
        "Bytes",
        "KB",
        "MB",
        "GB"
    ];

    const index =
        Math.floor(
            Math.log(bytes) /
            Math.log(1024)
        );

    return (
        parseFloat(
            (bytes /
                Math.pow(1024, index))
                .toFixed(2)
        )
        + " "
        + units[index]
    );
}


function escapeHtml(value) {

    return String(value)
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;")
        .replace(/"/g, "&quot;")
        .replace(/'/g, "&#039;");
}