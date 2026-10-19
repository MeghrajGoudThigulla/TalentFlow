// TalentFlow Client Application
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

// --- Initialization ---
document.addEventListener('DOMContentLoaded', async () => {
  renderAuthHeader();
  await loadPipelineStages();
  await loadJobs();
  await initSignalR();

  // If no user, default to recruiter for smooth preview
  if (!state.user) {
    await demoLogin('recruiter@talentflow.local', 'Recruiter@123456');
  }
});

// --- Auth Functions ---
async function demoLogin(email, password) {
  try {
    const res = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });

    if (!res.ok) {
      const err = await res.json();
      showToast(err.detail || 'Login failed', 'error');
      return;
    }

    const data = await res.json();
    setSession(data);
    showToast(`Logged in as ${data.fullName} (${data.role})`, 'success');
    await loadJobs();
    if (state.selectedJobId) {
      await reloadBoard();
    }
  } catch (err) {
    showToast('Failed to connect to authentication server', 'error');
  }
}

function setSession(data) {
  state.token = data.accessToken;
  state.refreshToken = data.refreshToken;
  state.user = { id: data.userId, email: data.email, fullName: data.fullName, role: data.role };

  localStorage.setItem('tf_token', state.token);
  localStorage.setItem('tf_refresh_token', state.refreshToken);
  localStorage.setItem('tf_user', JSON.stringify(state.user));

  renderAuthHeader();
}

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

  state.token = '';
  state.refreshToken = '';
  state.user = null;
  localStorage.removeItem('tf_token');
  localStorage.removeItem('tf_refresh_token');
  localStorage.removeItem('tf_user');

  renderAuthHeader();
  showToast('Logged out successfully', 'success');
}

function renderAuthHeader() {
  const outView = document.getElementById('logged-out-view');
  const inView = document.getElementById('logged-in-view');

  if (state.user) {
    outView.classList.add('hidden');
    inView.classList.remove('hidden');
    document.getElementById('user-display-name').textContent = state.user.fullName;
    document.getElementById('user-display-role').textContent = state.user.role;
  } else {
    outView.classList.remove('hidden');
    inView.classList.add('hidden');
  }
}

function authHeaders() {
  return state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
}

// --- Navigation Tabs ---
function switchTab(tabName) {
  document.querySelectorAll('.nav-tab').forEach(t => t.classList.remove('active'));
  document.querySelectorAll('.view-section').forEach(s => s.classList.add('hidden'));

  document.getElementById(`tab-${tabName}`).classList.add('active');
  document.getElementById(`section-${tabName}`).classList.remove('hidden');

  if (tabName === 'kanban') reloadBoard();
  if (tabName === 'jobs') loadManageJobs();
  if (tabName === 'careers') loadPublicCareers();
  if (tabName === 'reports') loadReports();
}

// --- Real-time SignalR ---
async function initSignalR() {
  const bar = document.getElementById('connection-bar');
  const text = document.getElementById('connection-text');

  if (typeof signalR === 'undefined') {
    text.textContent = 'SignalR client library loading...';
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
      showToast(`Card moved: ${data.candidateName} -> ${data.toStageName}`, 'info');
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    state.hubConnection.on('NoteAdded', (data) => {
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    state.hubConnection.on('InterviewScheduled', (data) => {
      if (state.selectedJobId === data.jobId) reloadBoard();
    });

    await state.hubConnection.start();
    bar.classList.add('connected');
    text.textContent = 'Live WebSockets SignalR Connected - Pipeline synchronization active';

    if (state.selectedJobId) {
      await state.hubConnection.invoke('JoinJobBoard', state.selectedJobId);
    }
  } catch (err) {
    bar.classList.remove('connected');
    text.textContent = 'Live updates standby (Polling mode ready)';
  }
}

// --- Data Loading ---
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
  selector.innerHTML = '';

  if (state.jobs.length === 0) {
    selector.innerHTML = '<option value="">No jobs found</option>';
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
  state.selectedJobId = selector.value;

  const currentJob = state.jobs.find(j => j.id === state.selectedJobId);
  const badge = document.getElementById('job-info-badge');
  if (currentJob) {
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
  container.innerHTML = '';

  state.stages.forEach(stage => {
    const col = document.createElement('div');
    col.className = 'kanban-column';
    col.dataset.stageId = stage.id;

    const stageApps = state.applications.filter(a => a.currentStageId === stage.id);

    col.innerHTML = `
      <div class="column-header">
        <span>${stage.name}</span>
        <span class="column-count">${stageApps.length}</span>
      </div>
      <div class="column-cards" id="cards-stage-${stage.id}" ondragover="onDragOver(event)" ondrop="onDropCard(event, '${stage.id}')">
      </div>
    `;

    const cardsContainer = col.querySelector('.column-cards');

    stageApps.forEach(app => {
      const card = document.createElement('div');
      card.className = 'candidate-card';
      card.draggable = true;
      card.ondragstart = (e) => onDragStart(e, app.id, app.concurrencyToken);
      card.onclick = () => openApplicationModal(app.id);

      const score = app.matchScore || 75;
      const scoreClass = score >= 85 ? 'badge-score-high' : score >= 70 ? 'badge-score-mid' : 'badge-score-low';

      card.innerHTML = `
        <div style="display: flex; justify-content: space-between; align-items: flex-start;">
          <div class="candidate-name">${escapeHtml(app.candidateName)}</div>
          <span class="badge ${scoreClass}">${score}% Match</span>
        </div>
        <div class="candidate-meta">${escapeHtml(app.candidateEmail)}</div>
        <div class="card-badges">
          <span class="badge">${new Date(app.appliedAt).toLocaleDateString()}</span>
          ${app.notesCount > 0 ? `<span class="badge badge-info">📝 ${app.notesCount}</span>` : ''}
          ${app.interviewsCount > 0 ? `<span class="badge badge-warning">🎯 ${app.interviewsCount} int</span>` : ''}
          ${app.hasOfferApproval ? `<span class="badge badge-success">✓ Offer Approved</span>` : ''}
        </div>
      `;
      cardsContainer.appendChild(card);
    });

    container.appendChild(col);
  });
}

// --- Drag & Drop ---
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

  await executeStageMove(app.id, targetStageId, 'Moved via Kanban Drag-and-Drop', draggedToken, false);
}

// --- Application Modal & Details ---
async function openApplicationModal(appId) {
  try {
    const res = await fetch(`/api/applications/${appId}`, { headers: authHeaders() });
    if (!res.ok) {
      showToast('Could not load application details', 'error');
      return;
    }

    const app = await res.json();
    state.currentModalApp = app;

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
    const scoreClass = score >= 85 ? 'badge-score-high' : score >= 70 ? 'badge-score-mid' : 'badge-score-low';
    scoreBadge.className = `badge ${scoreClass}`;
    scoreBadge.textContent = `${score}% Match`;

    const scoreBar = document.getElementById('app-modal-match-bar');
    scoreBar.style.width = `${score}%`;
    scoreBar.className = `match-meter-bar ${score >= 85 ? 'match-meter-high' : score >= 70 ? 'match-meter-mid' : 'match-meter-low'}`;

    const skillsContainer = document.getElementById('app-modal-matched-skills');
    const skills = (app.matchedSkills || 'Core Qualifications, Full-Stack Foundation').split(',').map(s => s.trim()).filter(Boolean);
    skillsContainer.innerHTML = skills.map(s => `<span class="skill-tag">${escapeHtml(s)}</span>`).join('');

    // Resume link
    const resumeLink = document.getElementById('app-modal-resume-link');
    if (app.resumePath) {
      resumeLink.href = `/api/applications/${app.id}/resume`;
      resumeLink.style.display = 'inline-flex';
      resumeLink.textContent = `Download ${app.resumeOriginalFileName || 'Resume'}`;
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
        <div class="badge badge-success mb-1">Approved by ${escapeHtml(app.offerApproval.approvedByName)}</div>
        <p class="text-muted" style="font-size: 0.8rem; margin-bottom: 0.5rem;">${new Date(app.offerApproval.approvedAt).toLocaleString()}</p>
        <button class="btn btn-sm btn-primary" onclick="window.open('/api/applications/${app.id}/offer-letter?rawHtml=true', '_blank')">📄 View Official Offer Letter</button>
      `;
    } else {
      offerBox.innerHTML = `
        <button class="btn btn-sm btn-outline" onclick="submitOfferApproval('${app.id}')">Approve Offer (Hiring Manager)</button>
      `;
    }

    renderNotes(app.notes || []);
    renderInterviews(app.interviews || []);
    renderHistory(app.transitions || []);

    openModal('application-modal');
  } catch (err) {
    showToast('Failed to open candidate card', 'error');
  }
}

async function submitStageMove() {
  if (!state.currentModalApp) return;

  const targetStageId = document.getElementById('app-modal-target-stage').value;
  const reason = document.getElementById('app-modal-move-reason').value;
  const allowSkip = document.getElementById('app-modal-allow-skip').checked;

  await executeStageMove(
    state.currentModalApp.id,
    targetStageId,
    reason,
    state.currentModalApp.concurrencyToken,
    allowSkip
  );
}

async function executeStageMove(appId, targetStageId, reason, token, allowSkip) {
  try {
    const res = await fetch(`/api/applications/${appId}/move`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({
        targetStageId,
        reason: reason || 'Pipeline progression',
        concurrencyToken: token,
        allowSkip
      })
    });

    if (!res.ok) {
      const err = await res.json();
      showToast(err.detail || 'Stage transition failed.', 'error');
      await reloadBoard();
      return;
    }

    showToast('Application stage successfully updated! Candidate notified.', 'success');
    closeModal('application-modal');
    await reloadBoard();
  } catch (err) {
    showToast('Network error during move', 'error');
  }
}

async function submitNote() {
  if (!state.currentModalApp) return;
  const input = document.getElementById('note-body-input');
  const body = input.value.trim();
  if (!body) return;

  try {
    const res = await fetch(`/api/applications/${state.currentModalApp.id}/notes`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({ body })
    });

    if (res.ok) {
      input.value = '';
      showToast('Note added successfully', 'success');
      // Refresh modal
      openApplicationModal(state.currentModalApp.id);
    }
  } catch (e) {
    showToast('Failed to save note', 'error');
  }
}

async function submitOfferApproval(appId) {
  try {
    const res = await fetch(`/api/applications/${appId}/offer/approve`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({ notes: 'Offer terms confirmed.' })
    });

    if (res.ok) {
      showToast('Offer approved by Hiring Manager!', 'success');
      openApplicationModal(appId);
      reloadBoard();
    } else {
      const err = await res.json();
      showToast(err.detail || 'Offer approval failed', 'error');
    }
  } catch (e) {
    showToast('Error approving offer', 'error');
  }
}

function renderNotes(notes) {
  const container = document.getElementById('notes-list');
  if (notes.length === 0) {
    container.innerHTML = '<p class="text-muted" style="font-size:0.85rem;">No notes yet.</p>';
    return;
  }
  container.innerHTML = notes.map(n => `
    <div class="note-item">
      <div class="note-author">${escapeHtml(n.authorName)} • ${new Date(n.createdAt).toLocaleString()}</div>
      <div>${escapeHtml(n.body)}</div>
    </div>
  `).join('');
}

function renderInterviews(interviews) {
  const container = document.getElementById('interviews-list');
  if (interviews.length === 0) {
    container.innerHTML = '<p class="text-muted" style="font-size:0.85rem;">No interviews scheduled.</p>';
    return;
  }
  container.innerHTML = interviews.map(i => `
    <div class="note-item mb-1">
      <strong>${new Date(i.startsAt).toLocaleString()} (${i.durationMinutes} min)</strong>
      <div>Interviewer(s): ${i.interviewerNames ? i.interviewerNames.join(', ') : 'Assigned team'}</div>
      ${i.locationOrLink ? `<div>Link: <a href="${escapeHtml(i.locationOrLink)}" target="_blank">${escapeHtml(i.locationOrLink)}</a></div>` : ''}
      <div class="mt-1">
        ${i.feedbacks && i.feedbacks.length > 0
          ? `<strong>Feedback:</strong> ${i.feedbacks.map(f => `Rating: ${f.rating}/5 (${f.recommendation}) - "${escapeHtml(f.comments)}"`).join('<br>')}`
          : `<button class="btn btn-sm btn-outline mt-1" onclick="openFeedbackPrompt('${i.id}')">+ Submit Feedback</button>`}
      </div>
    </div>
  `).join('');
}

async function openFeedbackPrompt(interviewId) {
  const ratingStr = prompt('Rating (1 to 5):', '5');
  if (!ratingStr) return;
  const rating = parseInt(ratingStr, 10);
  const comments = prompt('Comments / Evaluation:', 'Strong technical foundation and clear communication.');
  if (!comments) return;

  try {
    const res = await fetch(`/api/interviews/${interviewId}/feedback`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({
        rating,
        recommendation: rating >= 4 ? 1 : 3, // StrongYes or No
        comments
      })
    });

    if (res.ok) {
      showToast('Interview feedback submitted!', 'success');
      if (state.currentModalApp) openApplicationModal(state.currentModalApp.id);
    } else {
      const err = await res.json();
      showToast(err.detail || 'Feedback submission failed', 'error');
    }
  } catch (e) {
    showToast('Error submitting feedback', 'error');
  }
}

function renderHistory(transitions) {
  const container = document.getElementById('history-list');
  if (transitions.length === 0) {
    container.innerHTML = '<p class="text-muted" style="font-size:0.85rem;">No history records.</p>';
    return;
  }
  container.innerHTML = transitions.map(t => `
    <div class="history-item">
      <strong>${t.fromStageName} ➔ ${t.toStageName}</strong>
      <div class="text-muted" style="font-size:0.75rem;">By ${escapeHtml(t.actorName)} • ${new Date(t.occurredAt).toLocaleString()}</div>
      ${t.reason ? `<div style="font-size:0.8rem;">Reason: ${escapeHtml(t.reason)}</div>` : ''}
    </div>
  `).join('');
}

function switchDetailTab(tab) {
  document.querySelectorAll('.tab-pill').forEach(p => p.classList.remove('active'));
  document.getElementById('detail-tab-notes').classList.add('hidden');
  document.getElementById('detail-tab-interviews').classList.add('hidden');
  document.getElementById('detail-tab-history').classList.add('hidden');

  event.target.classList.add('active');
  document.getElementById(`detail-tab-${tab}`).classList.remove('hidden');
}

// --- Public Careers Portal ---
async function loadPublicCareers() {
  const container = document.getElementById('public-jobs-list');
  container.innerHTML = 'Loading open positions...';

  try {
    const res = await fetch('/api/public/jobs');
    if (res.ok) {
      const data = await res.json();
      const jobs = data.items || [];
      if (jobs.length === 0) {
        container.innerHTML = '<p>No open positions at this moment. Please check back soon.</p>';
        return;
      }
      container.innerHTML = jobs.map(j => `
        <div class="career-job-card">
          <div>
            <h3>${escapeHtml(j.title)}</h3>
            <div class="career-job-meta">${escapeHtml(j.department)} • ${escapeHtml(j.location)} • ${escapeHtml(j.employmentType)}</div>
            ${j.isSalaryDisclosed && j.salaryMin && j.salaryMax ? `<div class="badge badge-info mb-1">₹${(j.salaryMin/100000).toFixed(1)}L - ₹${(j.salaryMax/100000).toFixed(1)}L PA</div>` : ''}
            <div class="career-job-desc">${escapeHtml(j.description)}</div>
          </div>
          <button class="btn btn-primary" onclick="openApplyModal('${j.id}', '${escapeHtml(j.title)}')">Apply Now</button>
        </div>
      `).join('');
    }
  } catch (e) {
    container.innerHTML = 'Failed to load career listings.';
  }
}

function openApplyModal(jobId, jobTitle) {
  document.getElementById('apply-job-id').value = jobId;
  document.getElementById('apply-modal-title').textContent = `Apply for ${jobTitle}`;
  openModal('apply-modal');
}

async function submitApplication(e) {
  e.preventDefault();
  const btn = document.getElementById('btn-submit-apply');
  btn.disabled = true;
  btn.textContent = 'Submitting...';

  const jobId = document.getElementById('apply-job-id').value;
  const formData = new FormData();
  formData.append('fullName', document.getElementById('apply-name').value);
  formData.append('email', document.getElementById('apply-email').value);
  formData.append('phone', document.getElementById('apply-phone').value);
  formData.append('coverNote', document.getElementById('apply-covernote').value);
  formData.append('resume', document.getElementById('apply-resume').files[0]);

  try {
    const res = await fetch(`/api/public/jobs/${jobId}/apply`, {
      method: 'POST',
      body: formData
    });

    if (res.ok) {
      showToast('Application submitted successfully! Confirmation email queued.', 'success');
      closeModal('apply-modal');
      document.getElementById('apply-form').reset();
    } else {
      const err = await res.json();
      showToast(err.detail || 'Application failed', 'error');
    }
  } catch (err) {
    showToast('Failed to submit application', 'error');
  } finally {
    btn.disabled = false;
    btn.textContent = 'Submit Application';
  }
}

// --- Jobs Management ---
async function loadManageJobs() {
  const tbody = document.getElementById('jobs-table-body');
  tbody.innerHTML = '<tr><td colspan="8">Loading jobs...</td></tr>';

  try {
    const res = await fetch('/api/jobs?pageSize=100', { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      const jobs = data.items || [];
      tbody.innerHTML = jobs.map(j => `
        <tr>
          <td><strong>${escapeHtml(j.title)}</strong></td>
          <td>${escapeHtml(j.department)}</td>
          <td>${escapeHtml(j.location)}</td>
          <td><span class="badge ${j.status === 1 ? 'badge-success' : 'badge-warning'}">${j.status === 1 ? 'Open' : j.status === 0 ? 'Draft' : 'Closed'}</span></td>
          <td>${j.openings}</td>
          <td>${j.totalApplications}</td>
          <td>${new Date(j.createdAt).toLocaleDateString()}</td>
          <td>
            ${j.status === 0 ? `<button class="btn btn-sm btn-outline" onclick="publishJob('${j.id}')">Publish</button>` : ''}
            ${j.status === 1 ? `<button class="btn btn-sm btn-outline" onclick="closeJob('${j.id}')">Close</button>` : ''}
          </td>
        </tr>
      `).join('');
    }
  } catch (e) {
    tbody.innerHTML = '<tr><td colspan="8">Failed to load jobs</td></tr>';
  }
}

async function publishJob(id) {
  const res = await fetch(`/api/jobs/${id}/publish`, { method: 'POST', headers: authHeaders() });
  if (res.ok) {
    showToast('Job published to public careers portal!', 'success');
    await loadJobs();
    loadManageJobs();
  }
}

async function closeJob(id) {
  const res = await fetch(`/api/jobs/${id}/close`, { method: 'POST', headers: authHeaders() });
  if (res.ok) {
    showToast('Job marked as Closed', 'success');
    await loadJobs();
    loadManageJobs();
  }
}

function openCreateJobModal() {
  openModal('job-create-modal');
}

async function submitCreateJob(e) {
  e.preventDefault();
  const payload = {
    title: document.getElementById('job-title').value,
    department: document.getElementById('job-dept').value,
    location: document.getElementById('job-location').value,
    employmentType: document.getElementById('job-type').value,
    salaryMin: parseFloat(document.getElementById('job-sal-min').value) || null,
    salaryMax: parseFloat(document.getElementById('job-sal-max').value) || null,
    isSalaryDisclosed: true,
    openings: parseInt(document.getElementById('job-openings').value, 10) || 1,
    description: document.getElementById('job-desc').value,
    hiringManagerId: state.user?.id || null
  };

  try {
    const res = await fetch('/api/jobs', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify(payload)
    });

    if (res.ok) {
      showToast('Job created as Draft!', 'success');
      closeModal('job-create-modal');
      await loadJobs();
      if (document.getElementById('section-jobs').classList.contains('hidden') === false) {
        loadManageJobs();
      }
    } else {
      const err = await res.json();
      showToast(err.detail || 'Failed to create job', 'error');
    }
  } catch (e) {
    showToast('Network error creating job', 'error');
  }
}

// --- Schedule Interview ---
function openScheduleInterviewModal() {
  const now = new Date();
  now.setDate(now.getDate() + 1);
  now.setHours(14, 0, 0, 0);
  document.getElementById('int-datetime').value = now.toISOString().slice(0, 16);
  openModal('schedule-interview-modal');
}

async function submitInterviewSchedule(e) {
  e.preventDefault();
  if (!state.currentModalApp) return;

  const startsAt = document.getElementById('int-datetime').value;
  const duration = parseInt(document.getElementById('int-duration').value, 10);
  const type = parseInt(document.getElementById('int-type').value, 10);
  const link = document.getElementById('int-link').value;

  try {
    const res = await fetch(`/api/applications/${state.currentModalApp.id}/interviews`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        ...authHeaders()
      },
      body: JSON.stringify({
        startsAt: new Date(startsAt).toISOString(),
        durationMinutes: duration,
        type,
        locationOrLink: link,
        interviewerIds: [state.user.id]
      })
    });

    if (res.ok) {
      showToast('Interview scheduled and invite queued!', 'success');
      closeModal('schedule-interview-modal');
      openApplicationModal(state.currentModalApp.id);
      reloadBoard();
    } else {
      const err = await res.json();
      showToast(err.detail || 'Interview scheduling conflict', 'error');
    }
  } catch (e) {
    showToast('Failed to schedule interview', 'error');
  }
}

// --- Reports & Audit ---
async function loadReports() {
  if (state.selectedJobId) {
    try {
      const res = await fetch(`/api/reports/pipeline?jobId=${state.selectedJobId}`, { headers: authHeaders() });
      if (res.ok) {
        const data = await res.json();
        const stageCounts = Object.entries(data.stageCounts || {})
          .map(([stage, count]) => `<div><strong>${stage}:</strong> ${count}</div>`)
          .join('');
        document.getElementById('pipeline-funnel-content').innerHTML = `
          <h4>${escapeHtml(data.jobTitle)} (Total: ${data.totalApplications})</h4>
          <div style="margin-top:0.5rem; display:grid; grid-template-columns: repeat(3, 1fr); gap: 0.5rem;">
            ${stageCounts}
          </div>
        `;
      }
    } catch (e) {}
  }

  try {
    const res = await fetch('/api/reports/time-to-hire', { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      document.getElementById('time-to-hire-content').innerHTML = data.map(d => `
        <div style="margin-bottom:0.5rem;">
          <strong>${escapeHtml(d.jobTitle)}:</strong> ${d.averageDaysToHire} days (${d.totalHired} hired)
        </div>
      `).join('') || 'No hire records yet.';
    }
  } catch (e) {}

  try {
    const res = await fetch('/api/audit?pageSize=30', { headers: authHeaders() });
    if (res.ok) {
      const data = await res.json();
      const tbody = document.getElementById('audit-table-body');
      tbody.innerHTML = (data.items || []).map(a => `
        <tr>
          <td>${new Date(a.occurredAt).toLocaleTimeString()}</td>
          <td>${escapeHtml(a.actorEmail || 'System')}</td>
          <td>${escapeHtml(a.entityType)}</td>
          <td><span class="badge">${escapeHtml(a.action)}</span></td>
          <td style="font-size:0.75rem; color:#64748b;">${escapeHtml(a.afterJson || a.beforeJson || '-')}</td>
        </tr>
      `).join('') || '<tr><td colspan="5">No audit entries yet.</td></tr>';
    }
  } catch (e) {}
}

// --- Utilities ---
function openModal(id) { document.getElementById(id).classList.remove('hidden'); }
function closeModal(id) { document.getElementById(id).classList.add('hidden'); }

function showToast(msg, type = 'info') {
  const toast = document.getElementById('toast');
  toast.textContent = msg;
  toast.className = `toast ${type}`;
  toast.classList.remove('hidden');
  setTimeout(() => toast.classList.add('hidden'), 4000);
}

function escapeHtml(str) {
  if (!str) return '';
  return String(str)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
}
