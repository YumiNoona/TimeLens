const githubApi = 'https://api.github.com';
const minimumReleaseMajor = 5;

function repository() {
  return process.env.GITHUB_REPOSITORY || 'YumiNoona/TimeLens';
}

function requestHeaders(accept = 'application/vnd.github+json') {
  const headers = {
    Accept: accept,
    'User-Agent': 'TimeLens-Release-Service',
    'X-GitHub-Api-Version': '2022-11-28',
  };
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;
  return headers;
}

async function githubFetch(url, accept) {
  const response = await fetch(url, {
    headers: requestHeaders(accept),
    redirect: 'follow',
    cache: 'no-store',
  });
  if (!response.ok) {
    const privateHint = response.status === 404 && !process.env.GITHUB_TOKEN
      ? ' Configure GITHUB_TOKEN when the repository is private.'
      : '';
    throw new Error(`GitHub returned ${response.status}.${privateHint}`);
  }
  return response;
}

export function applicationAssetName() {
  return process.env.GITHUB_RELEASE_ASSET || 'TimeLens.exe';
}

export function installerAssetName() {
  return process.env.GITHUB_DOWNLOAD_ASSET || 'TimeLens-Setup.exe';
}

export async function getLatestRelease(assetName = applicationAssetName()) {
  const response = await githubFetch(`${githubApi}/repos/${repository()}/releases?per_page=50`);
  const releases = await response.json();
  const release = releases.find((candidate) => {
    const version = String(candidate.tag_name || '').replace(/^v/i, '');
    const assetNames = new Set((candidate.assets || []).map((asset) => asset.name));
    const major = Number(version.split('.')[0]);
    return !candidate.draft && !candidate.prerelease &&
      /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version) &&
      Number.isInteger(major) && major >= minimumReleaseMajor &&
      assetNames.has(applicationAssetName()) &&
      assetNames.has(installerAssetName()) &&
      assetNames.has(assetName) &&
      assetNames.has('SHA256SUMS.txt');
  });
  if (!release) throw new Error(`No production TimeLens release v${minimumReleaseMajor} or later contains the required assets.`);

  const asset = release.assets.find((candidate) => candidate.name === assetName);
  return { release, asset };
}

export async function getReleaseChecksum(release, assetName) {
  const checksumAsset = release.assets?.find((asset) => asset.name === 'SHA256SUMS.txt');
  if (!checksumAsset) throw new Error('The latest release does not contain SHA256SUMS.txt.');
  const response = await githubFetch(checksumAsset.url, 'application/octet-stream');
  const checksums = await response.text();
  const escapedName = assetName.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = checksums.match(new RegExp(`^([a-fA-F0-9]{64})\\s+\\*?${escapedName}$`, 'm'));
  if (!match) throw new Error(`SHA256SUMS.txt has no checksum for ${assetName}.`);
  return match[1].toLowerCase();
}

export async function assetRedirectUrl(asset) {
  const response = await fetch(asset.url, {
    headers: requestHeaders('application/octet-stream'),
    redirect: 'manual',
    cache: 'no-store',
  });
  if (response.status !== 302) {
    throw new Error(`GitHub asset authorization returned ${response.status} instead of a redirect.`);
  }
  const location = response.headers.get('location');
  if (!location) throw new Error('GitHub did not return a release download URL.');
  const downloadUrl = new URL(location);
  if (downloadUrl.protocol !== 'https:') throw new Error('GitHub returned a non-HTTPS asset URL.');
  return downloadUrl.toString();
}

export function releaseVersion(tagName) {
  const version = String(tagName || '').replace(/^v/i, '');
  if (!/^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/.test(version)) {
    throw new Error('The latest release tag is not a supported semantic version.');
  }
  return version;
}
