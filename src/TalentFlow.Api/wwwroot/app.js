// TalentFlow ATS Enterprise Client Application (IYOV AI Inspired)
let state = {
  token: localStorage.getItem('tf_token') || '',
  refreshToken: localStorage.getItem('tf_refresh_token') || '',
  user: JSON.parse(localStorage.getItem('tf_user') || 'null'),
  jobs: [],
  selectedJobId: null,
  stages: [],
  applications: [],
  currentModalApp: null,
  hubConnection: null
};

// --- DOM Ready Initialization ---
document.addEventListener('DOMContentLoaded', async () => {
  // Check if session exists in storage
  checkSessionState();

  // Pre-load public pipeline stages & jobs in background
  await loadPipelineStages();
  await loadJobs();

  // If already authenticated, establish SignalR connection
  if (state.user && state.token) {
    await initSignalR();
    if (state.selectedJobId) {
      await reloadBoard();
    }
  }
});

// --- Session & Authentication State Control ---
function checkSessionState() {
  const landingView = document.getElementById('landing-view');
  const appWorkspace = document.getElementById('app-workspace');
  const loginModal = document.getElementById('login-modal');

  if (state.user && state.token) {
    if (landingView) landingView.classList.add('hidden');
    if (appWorkspace) appWorkspace.classList.remove('hidden');
    if (loginModal) loginModal.classList.add('hidden');
    renderWorkspaceProfile();
  } else {
    if (landingView) landingView.classList.remove('hidden');
    if (appWorkspace) appWorkspace.classList.add('hidden');
    loadLandingPublicJobs();
  }
}

// Modal opening/closing controls
function openModal(modalId) {
  const modal = document.getElementById(modalId);
  if (modal) modal.classList.remove('hidden');
}

function openLoginModal() {
  openModal('login-modal');
  document.getElementById('auth-error-banner')?.classList.add('hidden');
  const emailInput = document.getElementById('login-email');
  if (emailInput) emailInput.focus();
}

function handleLoginBackdropClick(e) {
  if (e.target.id === 'login-modal') {
    closeModal('login-modal');
  }
}

function renderWorkspaceProfile() {
  if (!state.user) return;

  const displayName = document.getElementById('user-display-name');
  const displayRole = document.getElementById('user-display-role');
  const avatarInitials = document.getElementById('user-avatar-initials');

  displayName.textContent = state.user.fullName || state.user.email;
  displayRole.textContent = state.user.role || 'User';

  // Role pill styling
  displayRole.className = 'user-role-badge';
  if (state.user.role === 'Recruiter') displayRole.classList.add('role-recruiter');
  else if (state.user.role === 'HiringManager') displayRole.classList.add('role-hm');
  else displayRole.classList.add('role-admin');

  // Compute initials
  const names = (state.user.fullName || state.user.email || 'TF').split(' ');
  const initials = names.length > 1 ? (names[0][0] + names[1][0]).toUpperCase() : names[0].substring(0, 2).toUpperCase();
  avatarInitials.textContent = initials;
}

// Toggle password text visibility
function togglePasswordVisibility(inputId, btn) {
  const input = document.getElementById(inputId);
  if (input.type === 'password') {
    input.type = 'text';
    btn.innerHTML = `
      <svg class="eye-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19m-6.72-1.07a3 3 0 1 1-4.24-4.24"/><line x1="1" y1="1" x2="23" y2="23"/>
      </svg>
    `;
  } else {
    input.type = 'password';
    btn.innerHTML = `
      <svg class="eye-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
        <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
      </svg>
    `;
  }
}

// Switch between Staff Login and Candidate Careers in Auth screen
function switchAuthPortal(portal) {
  const staffBtn = document.getElementById('btn-toggle-staff');
  const careersBtn = document.getElementById('btn-toggle-careers');
  const staffSection = document.getElementById('auth-staff-section');
  const careersSection = document.getElementById('auth-careers-section');

  if (portal === 'staff') {
    staffBtn.classList.add('active');
    careersBtn.classList.remove('active');
    staffSection.classList.remove('hidden');
    careersSection.classList.add('hidden');
  } else {
    careersBtn.classList.add('active');
    staffBtn.classList.remove('active');
    careersSection.classList.remove('hidden');
    staffSection.classList.add('hidden');
    loadAuthPublicJobs();
  }
}

// Handle Form Submit for Login
async function handleFormLogin(e) {
  e.preventDefault();
  const email = document.getElementById('login-email').value.trim();
  const password = document.getElementById('login-password').value;

  const errorBanner = document.getElementById('auth-error-banner');
  const errorText = document.getElementById('auth-error-text');
  const submitBtn = document.getElementById('btn-login-submit');
  const btnText = document.getElementById('login-btn-text');
  const btnSpinner = document.getElementById('login-btn-spinner');

  errorBanner.classList.add('hidden');
  submitBtn.disabled = true;
  btnText.textContent = 'Authenticating...';
  btnSpinner.classList.remove('hidden');

  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });

    const data = await res.json();

    if (!res.ok) {
      errorText.textContent = data.detail || (data.errors ? Object.values(data.errors).flat().join(', ') : 'Invalid work email or password.');
      errorBanner.classList.remove('hidden');
      return;
    }

    // Set authenticated session
    setSession(data);
    closeModal('login-modal');
    showToast(`Welcome back, ${data.fullName}! Signed in as ${data.role}.`, 'success');

    // Transition to main workspace
    checkSessionState();
    await loadJobs();
    await initSignalR();
    if (state.selectedJobId) {
      await reloadBoard();
    }
  } catch (err) {
    errorText.textContent = 'Network or server error. Please verify the API is running.';
    errorBanner.classList.remove('hidden');
  } finally {
    submitBtn.disabled = false;
    btnText.textContent = 'Sign in to Platform';
    btnSpinner.classList.add('hidden');
  }
}

function setSession(data) {
  state.token = data.accessToken;
  state.refreshToken = data.refreshToken;
  state.user = { id: data.userId, email: data.email, fullName: data.fullName, role: data.role };

  localStorage.setItem('tf_token', state.token);
  localStorage.setItem('tf_refresh_token', state.refreshToken);
  localStorage.setItem('tf_user', JSON.stringify(state.user));
}

// User Logout
function logout() {
  if (state.refreshToken) {
    fetch('/api/auth/logout', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': `Bearer ${state.token}`
      },
      body: JSON.stringify({ refreshToken: state.refreshToken })
    }).catch(() => {});
  }

  // Disconnect SignalR hub if open
  if (state.hubConnection) {
    state.hubConnection.stop().catch(() => {});
    state.hubConnection = null;
  }

  state.token = '';
  state.refreshToken = '';
  state.user = null;
  localStorage.removeItem('tf_token');
  localStorage.removeItem('tf_refresh_token');
  localStorage.removeItem('tf_user');

  checkSessionState();
  showToast('Signed out successfully', 'info');
}

function authHeaders() {
  return state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
}

// --- Navigation Tabs inside Workspace ---
function switchTab(tabName) {
  document.querySelectorAll('.nav-btn').forEach(t => t.classList.remove('active'));
  document.querySelectorAll('.workspace-section').forEach(s => s.classList.add('hidden'));

  const activeBtn = document.getElementById(`tab-${tabName}`);
  const activeSec = document.getElementById(`section-${tabName}`);

  if (activeBtn) activeBtn.classList.add('active');
  if (activeSec) activeSec.classList.remove('hidden');

  if (tabName === 'kanban') reloadBoard();
  if (tabName === 'jobs') loadManageJobs();
  if (tabName === 'careers') loadPublicCareers();
  if (tabName === 'reports') loadReports();
}

// --- Real-Time SignalR WebSockets ---
async function initSignalR() {
  const livePill = document.getElementById('live-indicator');
  const liveText = document.getElementById('live-status-text');

  if (typeof signalR === 'undefined' || !state.token) {
    if (liveText) liveText.textContent = 'SignalR Ready';
    return;
  }

  try {
    state.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/pipeline', {
        accessTokenFactory: () => state.token
      })
      .withAutomaticReconnect()
      .build();

    state.hubConnection.on('ApplicationCreated', (data) => {
      showToast(`New candidate applied: ${data.candidateName} for ${data.jobTitle}`, 'info');
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    state.hubConnection.on('ApplicationMoved', (data) => {
      showToast(`Candidate moved: ${data.candidateName} → ${data.toStageName}`, 'info');
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    state.hubConnection.on('NoteAdded', (data) => {
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    state.hubConnection.on('InterviewScheduled', (data) => {
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    await state.hubConnection.start();
    if (livePill) {
      livePill.classList.add('connected');
      liveText.textContent = 'SignalR Live Connected';
    }

    if (state.selectedJobId) {
      await state.hubConnection.invoke('JoinJobBoard', state.selectedJobId);
    }
  } catch (err) {
    if (liveText) liveText.textContent = 'SignalR Reconnecting';
  }
}

// --- Data Loading & Board Pipeline ---
async function loadPipelineStages() {
  try {
    const res = await fetch('/api/pipeline-stages', { headers: authHeaders() });
    if (res.ok) {
      state.stages = await res.json();
    }
  } catch (e) {
    console.error('Failed to load stages', e);
  }
}

async function loadJobs() {
  try {
    const res = await fetch('/api/jobs?pageSize=50', { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      state.jobs = data.items || [];
      populateJobSelector();
    }
  } catch (e) {
    console.error('Failed to load jobs', e);
  }
}

function populateJobSelector() {
  const selector = document.getElementById('job-selector');
  if (!selector) return;
  selector.innerHTML = '';

  if (state.jobs.length === 0) {
    selector.innerHTML = '<option value="">No active requisitions</option>';
    return;
  }

  state.jobs.forEach(j => {
    const opt = document.createElement('option');
    opt.value = j.id;
    opt.textContent = `${j.title} (${j.department}) [${j.status === 1 ? 'Open' : j.status === 0 ? 'Draft' : 'Closed'}]`;
    selector.appendChild(opt);
  });

  if (!state.selectedJobId && state.jobs.length > 0) {
    state.selectedJobId = state.jobs[0].id;
  }
  selector.value = state.selectedJobId;
  onJobSelected();
}

async function onJobSelected() {
  const selector = document.getElementById('job-selector');
  if (!selector) return;
  state.selectedJobId = selector.value;

  const currentJob = state.jobs.find(j => j.id === state.selectedJobId);
  const badge = document.getElementById('job-stats-badge');
  if (currentJob && badge) {
    badge.textContent = `${currentJob.location} • ${currentJob.employmentType} • Openings: ${currentJob.openings}`;
  }

  if (state.hubConnection && state.hubConnection.state === 'Connected') {
    state.hubConnection.invoke('JoinJobBoard', state.selectedJobId).catch(() => {});
  }

  await reloadBoard();
}

async function reloadBoard() {
  if (!state.selectedJobId) return;

  try {
    const res = await fetch(`/api/jobs/${state.selectedJobId}/applications`, { headers: authHeaders() });
    if (res.ok) {
      state.applications = await res.json();
      renderKanbanBoard();
    }
  } catch (err) {
    console.error('Failed to reload board', err);
  }
}

function renderKanbanBoard() {
  const container = document.getElementById('kanban-columns');
  if (!container) return;
  container.innerHTML = '';

  state.stages.forEach(stage => {
    const col = document.createElement('div');
    col.className = 'kanban-col';
    col.dataset.stageId = stage.id;

    const stageApps = state.applications.filter(a => a.currentStageId === stage.id);

    col.innerHTML = `
      <div class="kanban-col-header">
        <div class="col-title-group">
          <span class="col-stage-dot" style="background: ${getStageColor(stage.name)};"></span>
          <h3>${stage.name}</h3>
        </div>
        <span class="col-count-pill">${stageApps.length}</span>
      </div>
      <div class="kanban-cards-stack" id="cards-stage-${stage.id}" ondragover="onDragOver(event)" ondrop="onDropCard(event, '${stage.id}')">
      </div>
    `;

    const cardsContainer = col.querySelector('.kanban-cards-stack');

    stageApps.forEach(app => {
      const card = document.createElement('div');
      card.className = 'kanban-card';
      card.draggable = true;
      card.ondragstart = (e) => onDragStart(e, app.id, app.concurrencyToken);
      card.onclick = () => openApplicationModal(app.id);

      const score = app.matchScore || 75;
      const scoreClass = score >= 85 ? 'score-high' : score >= 70 ? 'score-med' : 'score-low';

      // Candidate Initials
      const nameParts = (app.candidateName || 'Candidate').split(' ');
      const initials = nameParts.length > 1 ? (nameParts[0][0] + nameParts[1][0]).toUpperCase() : nameParts[0].substring(0, 2).toUpperCase();

      // Skills chips
      const skills = (app.matchedSkills || 'C#, .NET, Full-Stack').split(',').map(s => s.trim()).slice(0, 3);
      const skillsHtml = skills.map(s => `<span class="skill-tag">${escapeHtml(s)}</span>`).join('');

      card.innerHTML = `
        <div class="card-top-row">
          <div class="card-candidate-info">
            <div class="card-avatar">${initials}</div>
            <div>
              <div class="card-candidate-name">${escapeHtml(app.candidateName)}</div>
              <div class="card-applied-time">${new Date(app.appliedAt).toLocaleDateString()}</div>
            </div>
          </div>
          <span class="score-pill ${scoreClass}">★ ${score}%</span>
        </div>

        <div class="card-skills-row">
          ${skillsHtml}
        </div>

        <div class="card-bottom-row">
          <div class="card-meta-indicators">
            ${app.notesCount > 0 ? `<span>📝 ${app.notesCount}</span>` : ''}
            ${app.interviewsCount > 0 ? `<span>🎙️ ${app.interviewsCount}</span>` : ''}
            ${app.hasOfferApproval ? `<span style="color:#059669;font-weight:700;">✓ Offer</span>` : ''}
          </div>
          <div class="card-actions-quick">
            <button class="btn-icon-sm" title="View Candidate Drawer" onclick="event.stopPropagation(); openApplicationModal('${app.id}')">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>
            </button>
          </div>
        </div>
      `;
      cardsContainer.appendChild(card);
    });

    container.appendChild(col);
  });
}

function getStageColor(stageName) {
  switch (stageName.toLowerCase()) {
    case 'applied': return '#3b82f6';
    case 'screening': return '#8b5cf6';
    case 'interview': return '#f59e0b';
    case 'offer': return '#10b981';
    case 'hired': return '#059669';
    case 'rejected': return '#ef4444';
    default: return '#6b7280';
  }
}

// --- Drag & Drop Kanban Handlers ---
let draggedAppId = null;
let draggedToken = null;

function onDragStart(event, appId, token) {
  draggedAppId = appId;
  draggedToken = token;
  event.dataTransfer.setData('text/plain', appId);
}

function onDragOver(event) {
  event.preventDefault();
}

async function onDropCard(event, targetStageId) {
  event.preventDefault();
  if (!draggedAppId) return;

  const app = state.applications.find(a => a.id === draggedAppId);
  if (!app || app.currentStageId === targetStageId) return;

  await executeStageMove(app.id, targetStageId, 'Moved via Drag-and-Drop', draggedToken, false);
}

// Execute Stage Move with Concurrency Token
async function executeStageMove(appId, targetStageId, reason, concurrencyToken, allowSkip) {
  try {
    const res = await fetch(`/api/applications/${appId}/move`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({
        targetStageId,
        reason: reason || 'Progressed to next pipeline stage',
        concurrencyToken,
        allowRuleSkip: !!allowSkip
      })
    });

    if (res.status === 409) {
      showToast('Concurrency conflict! Card was updated by another recruiter. Refreshing...', 'error');
      await reloadBoard();
      return;
    }

    if (!res.ok) {
      const err = await res.json();
      showToast(err.detail || 'Stage transition rejected by validation rules.', 'error');
      return;
    }

    showToast('Candidate stage moved successfully', 'success');
    await reloadBoard();
    if (state.currentModalApp && state.currentModalApp.id === appId) {
      await openApplicationModal(appId);
    }
  } catch (err) {
    showToast('Failed to execute card move', 'error');
  }
}

// --- Candidate Detail Drawer ---
async function openApplicationModal(appId) {
  try {
    const res = await fetch(`/api/applications/${appId}`, { headers: authHeaders() });
    if (!res.ok) {
      showToast('Could not load candidate details', 'error');
      return;
    }

    const app = await res.json();
    state.currentModalApp = app;

    // Avatar
    const nameParts = (app.candidateName || 'Candidate').split(' ');
    const initials = nameParts.length > 1 ? (nameParts[0][0] + nameParts[1][0]).toUpperCase() : nameParts[0].substring(0, 2).toUpperCase();
    document.getElementById('app-modal-avatar').textContent = initials;

    document.getElementById('app-modal-candidate-name').textContent = app.candidateName;
    document.getElementById('app-modal-job-title').textContent = app.jobTitle;
    document.getElementById('app-modal-email').textContent = app.candidateEmail;
    document.getElementById('app-modal-phone').textContent = app.candidatePhone || 'Not provided';
    document.getElementById('app-modal-date').textContent = new Date(app.appliedAt).toLocaleString();
    document.getElementById('app-modal-stage').textContent = app.currentStageName;
    document.getElementById('app-modal-covernote').textContent = app.coverNote || 'No cover note provided.';

    // Populate Resume Match Score & Competencies
    const score = app.matchScore || 75;
    const scoreBadge = document.getElementById('app-modal-match-badge');
    scoreBadge.textContent = `${score}% Match`;
    scoreBadge.className = `score-badge ${score >= 85 ? 'score-high' : score >= 70 ? 'score-med' : 'score-low'}`;

    const scoreBar = document.getElementById('app-modal-match-bar');
    scoreBar.style.width = `${score}%`;

    const skillsContainer = document.getElementById('app-modal-matched-skills');
    const skills = (app.matchedSkills || 'C#, .NET, Clean Architecture, CQRS, SQL Server').split(',').map(s => s.trim()).filter(Boolean);
    skillsContainer.innerHTML = skills.map(s => `<span class="skill-tag">${escapeHtml(s)}</span>`).join('');

    // Resume link
    const resumeLink = document.getElementById('app-modal-resume-link');
    if (app.resumePath) {
      resumeLink.href = `/api/applications/${app.id}/resume`;
      resumeLink.style.display = 'inline-flex';
    } else {
      resumeLink.style.display = 'none';
    }

    // Populate stage options
    const stageSelect = document.getElementById('app-modal-target-stage');
    stageSelect.innerHTML = '';
    state.stages.forEach(s => {
      if (s.id !== app.currentStageId) {
        const opt = document.createElement('option');
        opt.value = s.id;
        opt.textContent = s.name;
        stageSelect.appendChild(opt);
      }
    });

    // Offer approval status
    const offerBox = document.getElementById('offer-status-content');
    if (app.offerApproval) {
      offerBox.innerHTML = `
        <div style="background:#ecfdf5;border:1px solid #a7f3d0;padding:0.65rem 0.85rem;border-radius:8px;margin-bottom:0.5rem;">
          <div style="color:#065f46;font-weight:700;font-size:0.85rem;">✓ Offer Approved by ${escapeHtml(app.offerApproval.approvedByName)}</div>
          <div style="color:#047857;font-size:0.75rem;">${new Date(app.offerApproval.approvedAt).toLocaleString()}</div>
        </div>
        <button class="btn-action btn-primary btn-full mt-1" onclick="window.open('/api/applications/${app.id}/offer-letter?rawHtml=true', '_blank')">
          📄 View Official Statutory Offer Letter
        </button>
      `;
    } else {
      offerBox.innerHTML = `
        <button class="btn-action btn-outline btn-full" onclick="submitOfferApproval('${app.id}')">
          Approve Employment Offer (Hiring Manager)
        </button>
      `;
    }

    renderNotes(app.notes || []);
    renderInterviews(app.interviews || []);
    renderHistory(app.transitions || []);

    document.getElementById('application-modal').classList.remove('hidden');
  } catch (err) {
    showToast('Failed to open candidate drawer', 'error');
  }
}

// Stage move from within drawer
async function submitStageMove() {
  if (!state.currentModalApp) return;

  const targetStageId = document.getElementById('app-modal-target-stage').value;
  const reason = document.getElementById('app-modal-move-reason').value.trim();
  const allowSkip = document.getElementById('app-modal-allow-skip').checked;

  await executeStageMove(state.currentModalApp.id, targetStageId, reason, state.currentModalApp.concurrencyToken, allowSkip);
}

// Drawer Tabs switcher
function switchDetailTab(tab) {
  document.querySelectorAll('.drawer-tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.drawer-tab-pane').forEach(p => p.classList.add('hidden'));

  const activeBtn = Array.from(document.querySelectorAll('.drawer-tab-btn')).find(b => b.textContent.toLowerCase().includes(tab));
  if (activeBtn) activeBtn.classList.add('active');

  const pane = document.getElementById(`detail-tab-${tab}`);
  if (pane) pane.classList.remove('hidden');
}

// Render Notes
function renderNotes(notes) {
  const container = document.getElementById('notes-list');
  if (notes.length === 0) {
    container.innerHTML = '<p class="text-muted text-xs">No notes added yet.</p>';
    return;
  }

  container.innerHTML = notes.map(n => `
    <div class="timeline-card">
      <div class="timeline-header">
        <span class="timeline-author">${escapeHtml(n.authorName)}</span>
        <span class="timeline-time">${new Date(n.createdAt).toLocaleDateString()}</span>
      </div>
      <p>${escapeHtml(n.body)}</p>
    </div>
  `).join('');
}

// Submit Internal Note
async function submitNote() {
  if (!state.currentModalApp) return;
  const bodyInput = document.getElementById('note-body-input');
  const body = bodyInput.value.trim();
  if (!body) return;

  try {
    const res = await fetch(`/api/applications/${state.currentModalApp.id}/notes`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({ body })
    });

    if (res.ok) {
      bodyInput.value = '';
      showToast('Note added successfully', 'success');
      await openApplicationModal(state.currentModalApp.id);
    }
  } catch (e) {
    showToast('Failed to add note', 'error');
  }
}

// Render Interviews
function renderInterviews(interviews) {
  const container = document.getElementById('interviews-list');
  if (interviews.length === 0) {
    container.innerHTML = '<p class="text-muted text-xs">No interviews scheduled yet.</p>';
    return;
  }

  const types = ['Phone Screen', 'Video Technical', 'Onsite Architectural'];

  container.innerHTML = interviews.map(i => `
    <div class="timeline-card">
      <div class="timeline-header">
        <span class="timeline-author">${types[i.type] || 'Interview Round'}</span>
        <span class="timeline-time">${new Date(i.scheduledAt).toLocaleString()} (${i.durationMinutes}m)</span>
      </div>
      <div class="mt-1">${i.meetingLink ? `<a href="${escapeHtml(i.meetingLink)}" target="_blank" class="link-btn">Join Video Room</a>` : 'Location: Onsite'}</div>
      ${i.feedbackList && i.feedbackList.length > 0 ? `
        <div class="mt-2" style="background:#ffffff;padding:0.5rem;border-radius:6px;border:1px solid #e2e8f0;">
          <span class="text-xs font-bold text-muted">Feedback (${i.feedbackList[0].interviewerName}): Score ${i.feedbackList[0].score}/5</span>
          <p class="text-xs mt-1">${escapeHtml(i.feedbackList[0].notes)}</p>
        </div>
      ` : `
        <button class="btn-action btn-outline btn-sm mt-2" onclick="promptSubmitFeedback('${i.id}')">Submit Interview Feedback</button>
      `}
    </div>
  `).join('');
}

// Submit Feedback Prompt
async function promptSubmitFeedback(interviewId) {
  const score = prompt('Enter score (1 to 5):', '5');
  if (!score) return;
  const notes = prompt('Enter feedback & assessment notes:', 'Strong technical execution, deep C# & systems understanding.');
  if (!notes) return;

  try {
    const res = await fetch(`/api/interviews/${interviewId}/feedback`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({
        score: parseInt(score, 10),
        strengths: 'Architecture, Clean Code',
        growthAreas: 'None',
        recommendation: 2, // StrongHire
        notes
      })
    });

    if (res.ok) {
      showToast('Interview feedback recorded', 'success');
      if (state.currentModalApp) await openApplicationModal(state.currentModalApp.id);
    }
  } catch (err) {
    showToast('Failed to submit feedback', 'error');
  }
}

// Render History
function renderHistory(transitions) {
  const container = document.getElementById('history-list');
  if (transitions.length === 0) {
    container.innerHTML = '<p class="text-muted text-xs">No stage history recorded yet.</p>';
    return;
  }

  container.innerHTML = transitions.map(t => `
    <div class="timeline-card">
      <div class="timeline-header">
        <span class="timeline-author">${escapeHtml(t.fromStageName || 'Applied')} → ${escapeHtml(t.toStageName)}</span>
        <span class="timeline-time">${new Date(t.createdAt).toLocaleString()}</span>
      </div>
      <p class="text-xs text-muted">${escapeHtml(t.reason || 'Card transition')} (by ${escapeHtml(t.movedByName)})</p>
    </div>
  `).join('');
}

// Submit Offer Approval
async function submitOfferApproval(appId) {
  const salary = prompt('Enter proposed Annual CTC in INR (e.g. 2800000):', '2800000');
  if (!salary) return;

  try {
    const res = await fetch(`/api/applications/${appId}/offer/approve`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({
        proposedSalary: parseFloat(salary),
        currency: 'INR',
        proposedStartDate: new Date(Date.now() + 30 * 24 * 3600 * 1000).toISOString(),
        notes: 'Approved offer based on strong competency match and technical score.'
      })
    });

    if (res.ok) {
      showToast('Offer approved! Statutory CTC offer letter generated.', 'success');
      await openApplicationModal(appId);
    } else {
      const err = await res.json();
      showToast(err.detail || 'Failed to approve offer.', 'error');
    }
  } catch (e) {
    showToast('Network error approving offer', 'error');
  }
}

// --- Jobs Management Section ---
async function loadManageJobs() {
  const tbody = document.getElementById('jobs-table-body');
  if (!tbody) return;
  tbody.innerHTML = '<tr><td colspan="8">Loading requisitions...</td></tr>';

  try {
    const res = await fetch('/api/jobs?pageSize=50', { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      const items = data.items || [];
      if (items.length === 0) {
        tbody.innerHTML = '<tr><td colspan="8">No job requisitions created yet.</td></tr>';
        return;
      }

      tbody.innerHTML = items.map(j => `
        <tr>
          <td><strong>${escapeHtml(j.title)}</strong></td>
          <td>${escapeHtml(j.department)}</td>
          <td>${escapeHtml(j.location)}</td>
          <td><span class="meta-badge">${j.status === 1 ? 'Open' : j.status === 0 ? 'Draft' : 'Closed'}</span></td>
          <td>${j.openings}</td>
          <td>${j.applicationsCount || 0}</td>
          <td>${j.publishedAt ? new Date(j.publishedAt).toLocaleDateString() : 'Unpublished'}</td>
          <td>
            ${j.status === 0 ? `<button class="btn-action btn-primary btn-sm" onclick="publishJob('${j.id}')">Publish</button>` : ''}
            ${j.status === 1 ? `<button class="btn-action btn-outline btn-sm" onclick="closeJob('${j.id}')">Close</button>` : ''}
          </td>
        </tr>
      `).join('');
    }
  } catch (err) {
    tbody.innerHTML = '<tr><td colspan="8">Failed to load jobs</td></tr>';
  }
}

async function publishJob(jobId) {
  try {
    const res = await fetch(`/api/jobs/${jobId}/publish`, { method: 'POST', headers: authHeaders() });
    if (res.ok) {
      showToast('Job requisition published to public careers portal', 'success');
      await loadJobs();
      await loadManageJobs();
    }
  } catch (err) {
    showToast('Failed to publish job', 'error');
  }
}

async function closeJob(jobId) {
  try {
    const res = await fetch(`/api/jobs/${jobId}/close`, { method: 'POST', headers: authHeaders() });
    if (res.ok) {
      showToast('Job requisition closed', 'info');
      await loadJobs();
      await loadManageJobs();
    }
  } catch (err) {
    showToast('Failed to close job', 'error');
  }
}

// --- Public Careers Portal View ---
async function loadPublicCareers() {
  const container = document.getElementById('public-jobs-list');
  if (!container) return;
  container.innerHTML = '<p>Loading active openings...</p>';

  try {
    const res = await fetch('/api/public/jobs');
    if (res.ok) {
      const data = await res.json();
      const items = data.items || [];
      if (items.length === 0) {
        container.innerHTML = '<p class="text-muted">No positions currently open.</p>';
        return;
      }

      container.innerHTML = items.map(j => `
        <div class="card-box">
          <div class="flex-between">
            <h3 style="font-size:1.15rem;font-weight:800;">${escapeHtml(j.title)}</h3>
            <span class="meta-badge">${escapeHtml(j.department)}</span>
          </div>
          <div class="text-muted text-xs mt-1">${escapeHtml(j.location)} • ${escapeHtml(j.employmentType)} • ${j.openings} opening(s)</div>
          ${j.salaryMin && j.salaryMax ? `<div style="color:#059669;font-weight:700;margin:0.5rem 0;">₹${(j.salaryMin/100000).toFixed(1)}L - ₹${(j.salaryMax/100000).toFixed(1)}L INR / yr</div>` : ''}
          <p class="text-muted text-xs mt-2" style="display:-webkit-box;-webkit-line-clamp:3;-webkit-box-orient:vertical;overflow:hidden;">${escapeHtml(j.description)}</p>
          <button class="btn-action btn-primary btn-full mt-4" onclick="openApplyModal('${j.id}', '${escapeHtml(j.title)}')">Apply for this Position</button>
        </div>
      `).join('');
    }
  } catch (e) {
    container.innerHTML = '<p>Failed to load public openings</p>';
  }
}

// --- Landing Page Public Careers Integration ---
let allLandingJobs = [];

async function loadLandingPublicJobs() {
  const container = document.getElementById('landing-public-jobs-list');
  if (!container) return;
  container.innerHTML = '<p style="color: #94a3b8; font-size: 0.9rem; grid-column: 1 / -1; text-align: center; padding: 2rem;">Loading current openings...</p>';

  try {
    const res = await fetch('/api/public/jobs');
    if (res.ok) {
      const data = await res.json();
      allLandingJobs = data.items || [];
      renderLandingJobs(allLandingJobs);
    } else {
      container.innerHTML = '<p style="color: #94a3b8; font-size: 0.9rem; grid-column: 1 / -1; text-align: center; padding: 2rem;">No open positions at this time. Please check back soon!</p>';
    }
  } catch (e) {
    container.innerHTML = '<p style="color: #94a3b8; font-size: 0.9rem; grid-column: 1 / -1; text-align: center; padding: 2rem;">Unable to load job openings.</p>';
  }
}

function filterLandingJobs() {
  const query = (document.getElementById('landing-job-search')?.value || '').toLowerCase().trim();
  const dept = (document.getElementById('landing-job-dept')?.value || '').trim();

  const filtered = allLandingJobs.filter(j => {
    const matchTitle = !query || j.title.toLowerCase().includes(query) || (j.description && j.description.toLowerCase().includes(query));
    const matchDept = !dept || j.department === dept;
    return matchTitle && matchDept;
  });

  renderLandingJobs(filtered);
}

function renderLandingJobs(jobs) {
  const container = document.getElementById('landing-public-jobs-list');
  if (!container) return;

  if (jobs.length === 0) {
    container.innerHTML = `
      <div style="grid-column: 1 / -1; text-align: center; padding: 3rem 1.5rem; background: rgba(255,255,255,0.02); border-radius: 16px; border: 1px dashed rgba(255,255,255,0.1);">
        <p style="color: #94a3b8; font-size: 0.95rem;">No open positions match your search criteria.</p>
      </div>`;
    return;
  }

  container.innerHTML = jobs.map(j => `
    <div class="landing-job-card">
      <div>
        <h4 class="landing-job-title">${escapeHtml(j.title)}</h4>
        <div class="landing-job-badges">
          <span class="landing-job-badge">${escapeHtml(j.department || 'Engineering')}</span>
          <span class="landing-job-badge">${escapeHtml(j.location || 'Remote')}</span>
          <span class="landing-job-badge">${escapeHtml(j.employmentType || 'Full-time')}</span>
          ${j.openings ? `<span class="landing-job-badge">${j.openings} Openings</span>` : ''}
          ${j.salaryMin && j.salaryMax ? `<span class="landing-job-badge" style="color: #34d399;">₹${(j.salaryMin/100000).toFixed(1)}L - ₹${(j.salaryMax/100000).toFixed(1)}L</span>` : ''}
        </div>
        <p style="font-size: 0.825rem; color: #94a3b8; line-height: 1.5; margin-bottom: 1.25rem;">
          ${escapeHtml(j.description ? (j.description.length > 130 ? j.description.substring(0, 130) + '...' : j.description) : 'Join our high-impact engineering team to build scalable recruitment workflows.')}
        </p>
      </div>
      <button type="button" class="btn-apply-job" onclick="openApplyModal('${j.id}', '${escapeHtml(j.title)}')">
        Apply with Resume →
      </button>
    </div>
  `).join('');
}

// --- Candidate Public Apply Modal ---
function openApplyModal(jobId, jobTitle) {
  document.getElementById('apply-job-id').value = jobId;
  document.getElementById('apply-modal-title').textContent = `Apply for: ${jobTitle}`;
  document.getElementById('apply-modal').classList.remove('hidden');
}

async function submitApplication(e) {
  e.preventDefault();
  const btn = document.getElementById('btn-submit-apply');
  btn.disabled = true;
  btn.textContent = 'Submitting & Scoring Resume...';

  const jobId = document.getElementById('apply-job-id').value;
  const formData = new FormData();
  formData.append('CandidateName', document.getElementById('apply-name').value.trim());
  formData.append('CandidateEmail', document.getElementById('apply-email').value.trim());
  formData.append('CandidatePhone', document.getElementById('apply-phone').value.trim());
  formData.append('CoverNote', document.getElementById('apply-covernote').value.trim());
  formData.append('Resume', document.getElementById('apply-resume').files[0]);

  try {
    const res = await fetch(`/api/public/jobs/${jobId}/apply`, {
      method: 'POST',
      body: formData
    });

    if (res.ok) {
      const result = await res.json();
      closeModal('apply-modal');
      document.getElementById('apply-form').reset();
      showToast(`Application submitted! AI Match Score: ${result.matchScore}%`, 'success');
    } else {
      const err = await res.json();
      showToast(err.detail || 'Failed to submit application', 'error');
    }
  } catch (err) {
    showToast('Network error during application submission', 'error');
  } finally {
    btn.disabled = false;
    btn.textContent = 'Submit Application';
  }
}

// --- Create Job Requisition ---
function openCreateJobModal() {
  document.getElementById('job-create-modal').classList.remove('hidden');
}

async function submitCreateJob(e) {
  e.preventDefault();
  const title = document.getElementById('job-title').value.trim();
  const department = document.getElementById('job-dept').value.trim();
  const location = document.getElementById('job-location').value.trim();
  const employmentType = document.getElementById('job-type').value;
  const openings = parseInt(document.getElementById('job-openings').value, 10);
  const salaryMin = parseFloat(document.getElementById('job-sal-min').value) || null;
  const salaryMax = parseFloat(document.getElementById('job-sal-max').value) || null;
  const description = document.getElementById('job-desc').value.trim();

  try {
    const res = await fetch('/api/jobs', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({
        title, department, location, employmentType, openings, salaryMin, salaryMax, isSalaryDisclosed: true, description
      })
    });

    if (res.ok) {
      closeModal('job-create-modal');
      document.getElementById('create-job-form').reset();
      showToast('Job requisition created (Draft status)', 'success');
      await loadJobs();
      await loadManageJobs();
    } else {
      showToast('Failed to create job', 'error');
    }
  } catch (err) {
    showToast('Network error creating job', 'error');
  }
}

// --- Schedule Interview Modal ---
function openScheduleInterviewModal() {
  document.getElementById('schedule-interview-modal').classList.remove('hidden');
}

async function submitInterviewSchedule(e) {
  e.preventDefault();
  if (!state.currentModalApp) return;

  const scheduledAt = new Date(document.getElementById('int-datetime').value).toISOString();
  const durationMinutes = parseInt(document.getElementById('int-duration').value, 10);
  const type = parseInt(document.getElementById('int-type').value, 10);
  const meetingLink = document.getElementById('int-link').value.trim();

  try {
    const res = await fetch(`/api/applications/${state.currentModalApp.id}/interviews`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', ...authHeaders() },
      body: JSON.stringify({
        scheduledAt, durationMinutes, type, meetingLink,
        participantUserIds: state.user ? [state.user.id] : []
      })
    });

    if (res.ok) {
      closeModal('schedule-interview-modal');
      showToast('Interview round confirmed & scheduled', 'success');
      await openApplicationModal(state.currentModalApp.id);
    } else {
      const err = await res.json();
      showToast(err.detail || 'Schedule conflict or validation failure.', 'error');
    }
  } catch (err) {
    showToast('Failed to schedule interview', 'error');
  }
}

// --- Reports & Analytics ---
async function loadReports() {
  try {
    const resP = await fetch('/api/reports/pipeline', { headers: authHeaders() });
    if (resP.ok) {
      const funnel = await resP.json();
      const funnelDiv = document.getElementById('pipeline-funnel-content');
      funnelDiv.innerHTML = funnel.map(f => `
        <div class="flex-between py-1 border-b" style="border-bottom:1px solid #e2e8f0;padding:0.4rem 0;">
          <span style="font-weight:600;">${escapeHtml(f.stageName)}</span>
          <span class="meta-badge">${f.candidateCount} candidate(s)</span>
        </div>
      `).join('');
    }

    const resT = await fetch('/api/reports/time-to-hire', { headers: authHeaders() });
    if (resT.ok) {
      const t = await resT.json();
      document.getElementById('time-to-hire-content').innerHTML = `
        <div style="font-size:2rem;font-weight:800;color:#2563eb;">${t.averageDaysToHire.toFixed(1)} <span style="font-size:1rem;color:#64748b;">days</span></div>
        <div class="text-muted text-xs mt-1">Based on ${t.totalHiresAnalyzed} completed hires across all requisitions.</div>
      `;
    }

    const resA = await fetch('/api/audit?pageSize=20', { headers: authHeaders() });
    if (resA.ok) {
      const audit = await resA.json();
      const tbody = document.getElementById('audit-table-body');
      tbody.innerHTML = (audit.items || []).map(a => `
        <tr>
          <td>${new Date(a.timestamp).toLocaleTimeString()}</td>
          <td><strong>${escapeHtml(a.actorName || a.actorEmail)}</strong></td>
          <td>${escapeHtml(a.entityName)}</td>
          <td><span class="meta-badge">${escapeHtml(a.action)}</span></td>
          <td class="text-xs text-muted">${escapeHtml(a.details || '')}</td>
        </tr>
      `).join('');
    }
  } catch (e) {
    console.error('Failed to load reports', e);
  }
}

// --- Helpers & Modals ---
function closeModal(modalId) {
  const modal = document.getElementById(modalId);
  if (modal) modal.classList.add('hidden');
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    closeModal('login-modal');
    closeModal('apply-modal');
    closeModal('application-modal');
    closeModal('job-create-modal');
    closeModal('schedule-interview-modal');
  }
});

function showToast(message, type = 'info') {
  const toast = document.getElementById('toast');
  toast.textContent = message;
  toast.className = `toast-popup ${type}`;
  toast.classList.remove('hidden');

  setTimeout(() => {
    toast.classList.add('hidden');
  }, 4000);
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/[&<>"']/g, function(m) {
    return {
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    }[m];
  });
}
