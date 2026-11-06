import { StatusBar } from "expo-status-bar";
import * as SecureStore from "expo-secure-store";
import * as Location from "expo-location";
import * as FileSystem from "expo-file-system/legacy";
import * as Sharing from "expo-sharing";
import * as ImagePicker from "expo-image-picker";
import * as ImageManipulator from "expo-image-manipulator";
import { HubConnectionBuilder, HttpTransportType, LogLevel } from "@microsoft/signalr";
import { useCallback, useEffect, useRef, useState } from "react";
import { AppState, useColorScheme, Image } from "react-native";
import { ActivityIndicator, Pressable, SafeAreaView, ScrollView, StyleSheet, Text, TextInput, View } from "react-native";
import TurnOvaLogo from "./TurnOvaLogo";
import { singleFlight } from "../shared/refreshCoordinator";
import { findActiveRide } from "../shared/rideRecovery";

type ThemePreference = "light" | "dark" | "system";
type ThemeColors = {
  background: string; header: string; surface: string; text: string; muted: string;
  border: string; accent: string; accentText: string; subtle: string; label: string;
  danger: string; success: string; tabInactive: string; tabActive: string;
};
const lightColors: ThemeColors = {
  background: "#F4F6F8", header: "#14283F", surface: "#FFFFFF", text: "#172B42",
  muted: "#718094", border: "#E2E8EE", accent: "#087BFA", accentText: "#FFFFFF",
  subtle: "#B8C5D2", label: "#8290A0", danger: "#B42318", success: "#237A54",
  tabInactive: "#8793A2", tabActive: "#087BFA"
};
const darkColors: ThemeColors = {
  background: "#0F1722", header: "#101D2B", surface: "#182536", text: "#E8EEF5",
  muted: "#A6B3C2", border: "#344457", accent: "#18DDB9", accentText: "#06243D",
  subtle: "#A6B3C2", label: "#A6B3C2", danger: "#FF8A80", success: "#70D6A0",
  tabInactive: "#9AA9B9", tabActive: "#18DDB9"
};
const API = process.env.EXPO_PUBLIC_API_BASE_URL ?? "http://10.0.2.2:5000";
const ZONE = "hyderabad-central";
const DEFAULT_SERVICE_CLASS = "cab";
type Tokens = { accessToken: string; refreshToken: string };
type User = { id: string; email: string; name?: string; role: string };
type QueueEntry = { driverId: string; position: number; joinedAt: string };
type QueueMembership = { isQueued: boolean; position: number | null };
type DriverRideHistoryItem = { rideId: string; pickup: string; dropoff: string; passengers: number; serviceClass: string; status: string; requestedAt: string; acceptedAt: string | null; locationSharingActive: boolean; stops?: string[]; negotiationExpiresAtUtc?: string | null };
type AgreementVersion = { rideId: string; version: number; amount: number; currency: string; pickup: string; dropoff: string; stops: string[]; proposedBy: string; createdAtUtc: string; riderConfirmed: boolean; driverConfirmed: boolean };

async function saveTokens(tokens: Tokens) {
  await SecureStore.setItemAsync("turnova.access", tokens.accessToken);
  await SecureStore.setItemAsync("turnova.refresh", tokens.refreshToken);
}
async function clearTokens() {
  await SecureStore.deleteItemAsync("turnova.access");
  await SecureStore.deleteItemAsync("turnova.refresh");
}
async function jsonRequest(path: string, init: RequestInit = {}, token?: string) {
  const response = await fetch(`${API}${path}`, {
    ...init,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}), ...init.headers }
  });
  const body = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(body.message ?? body.title ?? "Request failed.");
  return body;
}

export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [themePreference, setThemePreference] = useState<ThemePreference>("system");
  const systemColorScheme = useColorScheme();
  const [tokens, setTokens] = useState<Tokens | null>(null);
  const refreshInFlight = useRef<Promise<string> | null>(null);
  const [tab, setTab] = useState<"Home" | "Queue" | "Trips" | "Payslips" | "Profile">("Home");
  const [mode, setMode] = useState<"login" | "register">("login");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [name, setName] = useState("");
  const [queue, setQueue] = useState<QueueEntry[]>([]);
  const [membership, setMembership] = useState<QueueMembership>({ isQueued: false, position: null });
  const [availabilityStatus, setAvailabilityStatus] = useState<"Online" | "Offline">("Offline");
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [availableRides, setAvailableRides] = useState<any[]>([]);
  const [activeRide, setActiveRide] = useState<any>(null);
  const [driverRideHistory, setDriverRideHistory] = useState<DriverRideHistoryItem[]>([]);
  const [agreementHistory, setAgreementHistory] = useState<AgreementVersion[]>([]);
  const [agreementAmount, setAgreementAmount] = useState("");
  const [onboardingProfile, setOnboardingProfile] = useState<any>(null);
  const [unionAssignment, setUnionAssignment] = useState<any>(null);
  const [driverName, setDriverName] = useState("");
  const [phoneNumber, setPhoneNumber] = useState("");
  const [vehicleMake, setVehicleMake] = useState("");
  const [vehicleModel, setVehicleModel] = useState("");
  const [vehicleRegistration, setVehicleRegistration] = useState("");
  const [licenceReference, setLicenceReference] = useState("");
  const [registrationReference, setRegistrationReference] = useState("");
  const [profilePhotoBase64, setProfilePhotoBase64] = useState("");
  const [legacyServiceClass, setLegacyServiceClass] = useState<string | null>(null);
  const serviceClass = legacyServiceClass ?? DEFAULT_SERVICE_CLASS;

  useEffect(() => {
    let mounted = true;
    void SecureStore.getItemAsync("turnova.theme").then(value => {
      if (mounted && (value === "light" || value === "dark" || value === "system")) setThemePreference(value);
    }).catch(() => {});
    return () => { mounted = false; };
  }, []);

  const selectTheme = (preference: ThemePreference) => {
    setThemePreference(preference);
    void SecureStore.setItemAsync("turnova.theme", preference);
  };

  const acceptSession = useCallback(async (result: any) => {
    const next = { accessToken: result.accessToken, refreshToken: result.refreshToken };
    await saveTokens(next);
    setTokens(next);
    setUser(result.user);
    setError("");
  }, []);

  const refreshSession = useCallback(async (refreshToken: string) => {
    const result = await jsonRequest("/api/v1/auth/refresh", {
      method: "POST", body: JSON.stringify({ refreshToken })
    });
    await acceptSession(result);
    return result.accessToken as string;
  }, [acceptSession]);

  const apiFetch = useCallback(async (path: string, init: RequestInit = {}) => {
    if (!tokens) throw new Error("Please sign in again.");
    let response = await fetch(`${API}${path}`, {
      ...init,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${tokens.accessToken}`, ...init.headers }
    });
    if (response.status === 401) {
      const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
      if (!refreshToken) { await clearTokens(); setTokens(null); setUser(null); setActiveRide(null); setDriverRideHistory([]); setAvailableRides([]); setAgreementHistory([]); setAgreementAmount(""); setUnionAssignment(null); throw new Error("Session expired. Please sign in again."); }
      let accessToken: string;
      try {
        accessToken = await singleFlight(refreshInFlight, () => refreshSession(refreshToken));
      } catch {
        await clearTokens();
        setTokens(null);
        setUser(null);
        setActiveRide(null);
        setDriverRideHistory([]);
        setUnionAssignment(null);
        setAvailableRides([]);
        throw new Error("Session expired. Please sign in again.");
      }
      response = await fetch(`${API}${path}`, {
        ...init,
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${accessToken}`, ...init.headers }
      });
    }
    const body = await response.json().catch(() => ({}));
    if (!response.ok) throw new Error(body.message ?? body.title ?? "Request failed.");
    return body;
  }, [tokens, refreshSession]);

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        const accessToken = await SecureStore.getItemAsync("turnova.access");
        const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
        if (accessToken && refreshToken) {
          try {
            const me = await jsonRequest("/api/v1/auth/me", {}, accessToken);
            if (active) { setTokens({ accessToken, refreshToken }); setUser(me); }
          } catch {
            try {
              const result = await jsonRequest("/api/v1/auth/refresh", { method: "POST", body: JSON.stringify({ refreshToken }) });
              if (active) await acceptSession(result);
            } catch { await clearTokens(); }
          }
        }
      } catch (e) { if (active) setError("Unable to restore your session."); }
      finally { if (active) setLoading(false); }
    })();
    return () => { active = false; };
  }, [acceptSession]);

  const authenticate = async (replaceOldest = false) => {
    setLoading(true); setError(""); setNotice("");
    try {
      const path = mode === "register" ? "/api/v1/auth/driver/register" : "/api/v1/auth/driver/login";
      const result = await jsonRequest(path, {
        method: "POST",
        body: JSON.stringify({ email: email.trim(), password, name: name.trim() || null, replaceOldest })
      });
      await acceptSession(result);
    } catch (e) { setError(e instanceof Error ? e.message : "Authentication failed."); }
    finally { setLoading(false); }
  };

  const loadAvailability = useCallback(async () => {
    try { const result = await apiFetch("/api/v1/driver/availability"); setAvailabilityStatus(result.availabilityStatus === "Online" ? "Online" : "Offline"); }
    catch (e) { setError(e instanceof Error ? e.message : "Unable to load availability."); }
  }, [apiFetch]);
  useEffect(() => { if (user) void loadAvailability(); }, [user, loadAvailability]);

  const toggleAvailability = async () => {
    setLoading(true); setError(""); setNotice("");
    try {
      const nextOnline = availabilityStatus !== "Online";
      if (legacyServiceClass && nextOnline) {
        setError("Legacy non-Cab profiles are preserved for history and cannot go Online in the Cab-only MVP. You can still go Offline.");
        return;
      }
      let position: Location.LocationObject | undefined;
      if (nextOnline) {
        const permission = await Location.requestForegroundPermissionsAsync();
        if (!permission.granted)
          throw new Error("Location permission is required while online so riders can find nearby vehicles.");
        position = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
      }
      const result = await apiFetch("/api/v1/driver/availability", {
        method: "POST",
        body: JSON.stringify({
          isOnline: nextOnline,
          latitude: position?.coords.latitude,
          longitude: position?.coords.longitude
        })
      });
      setAvailabilityStatus(result.availabilityStatus);
      setNotice(nextOnline ? "You are online and have joined the back of the FIFO queue. Your location is shared only while online." : "You are offline and have left the FIFO queue.");
      await loadQueue();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to change availability."); }
    finally { setLoading(false); }
  };

  useEffect(() => {
    if (!user || availabilityStatus !== "Online" || legacyServiceClass ||
        (activeRide && ["Negotiating", "AgreementConfirmed", "Accepted", "Arrived", "InProgress"].includes(activeRide.status))) return;
    let mounted = true;
    let refreshing = false;
    let subscription: Location.LocationSubscription | undefined;

    const stopWatching = () => {
      subscription?.remove();
      subscription = undefined;
    };

    const publishLocation = async (latitude: number, longitude: number): Promise<boolean> => {
      if (!mounted || AppState.currentState !== "active") return false;
      try {
        await apiFetch("/api/v1/driver/location", {
          method: "POST",
          body: JSON.stringify({ latitude, longitude })
        });
        return true;
      } catch (e) {
        if (!mounted) return false;
        if (e instanceof Error && e.message.includes("left your assigned service zone")) {
          setAvailabilityStatus("Offline");
          setNotice("You were taken offline because your current location left your assigned service zone.");
        } else {
          setError(e instanceof Error ? e.message : "Unable to update availability location.");
        }
        return false;
      }
    };

    const startWatching = async () => {
      if (!mounted || subscription || AppState.currentState !== "active") return;
      const nextSubscription = await Location.watchPositionAsync(
        { accuracy: Location.Accuracy.Balanced, timeInterval: 15000, distanceInterval: 50 },
        location => { void publishLocation(location.coords.latitude, location.coords.longitude); }
      );
      if (!mounted || AppState.currentState !== "active") nextSubscription.remove();
      else subscription = nextSubscription;
    };

    const refreshForegroundLocation = async () => {
      if (!mounted || refreshing) return;
      refreshing = true;
      try {
        const permission = await Location.getForegroundPermissionsAsync();
        if (!permission.granted) {
          await apiFetch("/api/v1/driver/availability", {
            method: "POST",
            body: JSON.stringify({ isOnline: false })
          });
          if (mounted) {
            setAvailabilityStatus("Offline");
            setNotice("You were taken offline because location permission is required while online.");
          }
          return;
        }

        if (!mounted || AppState.currentState !== "active") return;
        const current = await Location.getCurrentPositionAsync({ accuracy: Location.Accuracy.Balanced });
        if (!mounted || AppState.currentState !== "active" ||
            !await publishLocation(current.coords.latitude, current.coords.longitude)) return;
        await startWatching();
      } catch (e) {
        if (mounted) setError(e instanceof Error ? e.message : "Unable to refresh availability location.");
      } finally {
        refreshing = false;
      }
    };

    const appStateSubscription = AppState.addEventListener("change", state => {
      if (state === "active") void refreshForegroundLocation();
      else stopWatching();
    });
    if (AppState.currentState === "active") void refreshForegroundLocation();

    return () => {
      mounted = false;
      stopWatching();
      appStateSubscription.remove();
    };
  }, [user?.id, availabilityStatus, legacyServiceClass, activeRide?.status, apiFetch]);

  const loadQueue = useCallback(async () => {
    if (legacyServiceClass) {
      setQueue([]);
      setMembership({ isQueued: false, position: null });
      return;
    }
    try {
      const [data, current] = await Promise.all([
        apiFetch(`/api/v1/zones/${ZONE}/queues/${serviceClass}`),
        apiFetch(`/api/v1/zones/${ZONE}/queues/${serviceClass}/me`)
      ]);
      setQueue(data);
      setMembership(current);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load queue."); }
  }, [apiFetch, serviceClass, legacyServiceClass]);
  useEffect(() => { if (user && tab === "Queue") void loadQueue(); }, [user, tab, loadQueue]);

  const leaveQueue = async () => {
    setLoading(true); setError(""); setNotice("");
    try {
      await apiFetch(`/api/v1/zones/${ZONE}/queues/${serviceClass}/me`, { method: "DELETE" });
      setNotice("You left the queue. You can join again whenever you're ready.");
      await loadQueue();
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to leave queue."); }
    finally { setLoading(false); }
  };

  const loadAvailableRides = useCallback(async () => {
    if (legacyServiceClass) { setAvailableRides([]); return; }
    try { setAvailableRides(await apiFetch("/api/v1/driver/rides/available")); }
    catch (e) { setError(e instanceof Error ? e.message : "Unable to load ride requests."); }
  }, [apiFetch, legacyServiceClass]);

  const loadDriverRideHistory = useCallback(async (isActive: () => boolean) => {
    const history = await apiFetch("/api/v1/driver/rides/mine") as DriverRideHistoryItem[];
    if (!isActive()) return;
    setDriverRideHistory(history);
    const active = findActiveRide(history, "driver");
    setActiveRide(active ?? null);
  }, [apiFetch]);

  const loadAgreementHistory = useCallback(async (rideId: string) => {
    const result = await apiFetch(`/api/v1/rides/${rideId}/agreements`) as AgreementVersion[];
    setAgreementHistory(result);
  }, [apiFetch]);

  useEffect(() => {
    if (!activeRide?.rideId) { setAgreementHistory([]); setAgreementAmount(""); return; }
    if (tab !== "Trips") return;
    let active = true;
    void apiFetch(`/api/v1/rides/${activeRide.rideId}/agreements`).then(result => {
      if (active) setAgreementHistory(result as AgreementVersion[]);
    }).catch(() => { if (active) setAgreementHistory([]); });
    return () => { active = false; };
  }, [activeRide?.rideId, tab, apiFetch]);

  const proposeAgreement = async () => {
    if (!activeRide?.rideId) return;
    const amount = Number(agreementAmount);
    if (!Number.isFinite(amount) || amount <= 0) { setError("Enter a positive proposed amount in INR."); return; }
    setError("");
    try {
      await apiFetch(`/api/v1/rides/${activeRide.rideId}/agreements`, {
        method: "POST",
        headers: { "Idempotency-Key": `driver-${Date.now()}-${Math.random().toString(36).slice(2)}` },
        body: JSON.stringify({ amount, currency: "INR" })
      });
      setAgreementAmount("");
      await loadAgreementHistory(activeRide.rideId);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to submit fare proposal."); }
  };

  const confirmAgreement = async () => {
    if (!activeRide?.rideId) return;
    const current = agreementHistory[agreementHistory.length - 1];
    if (!current) return;
    setError("");
    try {
      await apiFetch(`/api/v1/rides/${activeRide.rideId}/agreements/${current.version}/confirm`, { method: "POST" });
      await loadAgreementHistory(activeRide.rideId);
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to confirm fare agreement."); }
  };

  useEffect(() => {
    if (!user?.id || tab !== "Trips") return;
    let active = true;
    void loadDriverRideHistory(() => active).catch(e => {
      if (active) setError(e instanceof Error ? e.message : "Unable to load trip history.");
    });
    return () => { active = false; };
  }, [user?.id, tab, loadDriverRideHistory, activeRide?.status]);

  // SignalR is at-least-once: deduplicate by the server-issued offerId before surfacing it.
  const receivedOfferIds = useRef<Set<string>>(new Set());
  useEffect(() => {
    if (!user?.id || availabilityStatus !== "Online" || legacyServiceClass) return;
    let mounted = true;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?driverId=${encodeURIComponent(user.id)}`, {
        accessTokenFactory: async () => await SecureStore.getItemAsync("turnova.access") ?? tokens?.accessToken ?? "",
        transport: HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const refreshOffers = () => { if (mounted) void loadAvailableRides(); };
    const refreshDriverState = () => {
      if (!mounted) return;
      refreshOffers();
      void loadDriverRideHistory(() => mounted).catch(e => {
        if (mounted) setError(e instanceof Error ? e.message : "Unable to recover selected Rider bookings.");
      });
    };
    connection.on("RideSelected", (selection: { rideId?: string; expiresAtUtc?: string }) => {
      if (!mounted || !selection?.rideId) return;
      setNotice("A Rider selected you for a Cab trip. Review the request and negotiate directly.");
      void loadDriverRideHistory(() => mounted).catch(e => {
        if (mounted) setError(e instanceof Error ? e.message : "Unable to load selected Rider booking.");
      });
    });
    connection.on("RideOfferReceived", (offer: { rideId?: string; offerId?: string; expiresAtUtc?: string }) => {
      if (!mounted || !offer?.offerId || receivedOfferIds.current.has(offer.offerId)) return;
      receivedOfferIds.current.add(offer.offerId);
      if (receivedOfferIds.current.size > 500) {
        const oldest = receivedOfferIds.current.values().next().value;
        if (oldest) receivedOfferIds.current.delete(oldest);
      }
      setNotice("A new ride offer has arrived. Review the available ride requests.");
      refreshOffers();
    });
    // Recover selected bookings as well as legacy offers after initial connect/reconnect;
    // SignalR delivery is at-least-once and may be missed while the app is suspended.
    connection.onreconnected(refreshDriverState);
    void connection.start().then(refreshDriverState).catch(() => {
      if (mounted) setError("Live ride offers are temporarily unavailable. Available rides can still be refreshed manually.");
    });
    return () => {
      mounted = false;
      connection.off("RideOfferReceived");
      connection.off("RideSelected");
      void connection.stop();
    };
  }, [user?.id, availabilityStatus, legacyServiceClass, tokens?.accessToken, loadAvailableRides, loadDriverRideHistory]);

  useEffect(() => {
    if (!user?.id || tab !== "Trips" || !activeRide?.rideId || !tokens) return;
    let mounted = true;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?rideId=${encodeURIComponent(activeRide.rideId)}`, {
        accessTokenFactory: async () => await SecureStore.getItemAsync("turnova.access") ?? tokens.accessToken,
        transport: HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    const refreshAgreement = (update?: { rideId?: string }) => {
      if (mounted && (!update?.rideId || update.rideId === activeRide.rideId))
        void loadAgreementHistory(activeRide.rideId).catch(e => {
          if (mounted) setError(e instanceof Error ? e.message : "Unable to refresh fare agreement.");
        });
    };
    connection.on("RideAgreementChanged", refreshAgreement);
    connection.onreconnected(() => refreshAgreement());
    void connection.start().then(() => refreshAgreement()).catch(() => {
      if (mounted) setError("Live fare agreement updates are unavailable. Refresh the agreement manually.");
    });
    return () => { mounted = false; connection.off("RideAgreementChanged", refreshAgreement); void connection.stop(); };
  }, [user?.id, tab, activeRide?.rideId, tokens?.accessToken, loadAgreementHistory]);

  const acceptRide = async (rideId: string) => { try { const accepted = await apiFetch(`/api/v1/rides/${rideId}/accept`, { method: "POST" }); setActiveRide(accepted); setNotice("Ride accepted. Location sharing is active while this screen is open."); } catch (e) { setError(e instanceof Error ? e.message : "Unable to accept ride."); } };
  const rejectRide = async (rideId: string) => { try { await apiFetch(`/api/v1/rides/${rideId}/reject`, { method: "POST" }); setAvailableRides(current => current.filter(item => item.rideId !== rideId)); setNotice("Selected Rider request declined. No automatic reassignment was made."); if (activeRide?.rideId === rideId) { setActiveRide(null); setAgreementHistory([]); } } catch (e) { setError(e instanceof Error ? e.message : "Unable to decline ride."); } };
  useEffect(() => {
    if (!user || tab !== "Trips" || !activeRide) return;
    let mounted = true;
    const connection = new HubConnectionBuilder()
      .withUrl(`${API}/hubs/rides?rideId=${encodeURIComponent(activeRide.rideId)}`, {
        accessTokenFactory: async () => await SecureStore.getItemAsync("turnova.access") ?? tokens?.accessToken ?? "",
        transport: HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on("RideStatusChanged", (update: { rideId: string; status: string }) => {
      if (update.rideId === activeRide.rideId) setActiveRide((current: any) => current ? { ...current, status: update.status, locationSharingActive: ["Accepted", "Arrived", "InProgress"].includes(update.status) } : current);
    });
    connection.onreconnected(() => {
      if (mounted) void loadDriverRideHistory(() => mounted).catch(e => {
        if (mounted) setError(e instanceof Error ? e.message : "Unable to refresh trip history.");
      });
    });
    void connection.start().catch(() => {});
    return () => { mounted = false; void connection.stop(); };
  }, [user, tab, activeRide?.rideId, tokens?.accessToken, loadDriverRideHistory]);

  useEffect(() => { if (!user || tab !== "Trips" || !activeRide || !activeRide.locationSharingActive || !["Accepted", "Arrived", "InProgress"].includes(activeRide.status)) return; let subscription: Location.LocationSubscription | undefined; let mounted = true; (async () => { const permission = await Location.requestForegroundPermissionsAsync(); if (!permission.granted) { setError("Location permission is required to share your position with the rider."); return; } subscription = await Location.watchPositionAsync({ accuracy: Location.Accuracy.High, timeInterval: 5000, distanceInterval: 10 }, location => { if (mounted) void apiFetch(`/api/v1/rides/${activeRide.rideId}/location`, { method: "POST", body: JSON.stringify({ latitude: location.coords.latitude, longitude: location.coords.longitude }) }).catch(e => setError(e instanceof Error ? e.message : "Unable to update location.")); }); })(); return () => { mounted = false; subscription?.remove(); }; }, [user, tab, activeRide?.rideId, activeRide?.status, activeRide?.locationSharingActive, apiFetch]);

  const advanceRide = async (action: "arrive" | "start" | "complete") => { if (!activeRide) return; try { const updated = await apiFetch(`/api/v1/rides/${activeRide.rideId}/${action}`, { method: "POST" }); setActiveRide(updated); setNotice(action === "arrive" ? "Arrival confirmed." : action === "start" ? "Trip started." : "Trip completed. Location sharing stopped."); } catch (e) { setError(e instanceof Error ? e.message : "Unable to update ride status."); } };


  const downloadPayslip = async (year: number, month: number) => {
    if (!tokens) return;
    setLoading(true); setError(""); setNotice("");
    try {
      let accessToken = tokens.accessToken;
      let response = await fetch(`${API}/api/v1/driver/payslips/${year}/${month}.pdf`, {
        headers: { Authorization: `Bearer ${accessToken}` }
      });
      if (response.status === 401) {
        const refreshToken = await SecureStore.getItemAsync("turnova.refresh");
        if (!refreshToken) throw new Error("Session expired. Please sign in again.");
        accessToken = await refreshSession(refreshToken);
        response = await fetch(`${API}/api/v1/driver/payslips/${year}/${month}.pdf`, {
          headers: { Authorization: `Bearer ${accessToken}` }
        });
      }
      if (!response.ok) {
        const body = await response.json().catch(() => ({}));
        throw new Error(body.message ?? body.title ?? "Payslip is not available yet.");
      }
      const bytes = new Uint8Array(await response.arrayBuffer());
      const binary = Array.from(bytes, byte => String.fromCharCode(byte)).join("");
      const base64 = btoa(binary);
      const filename = `TurnOva-Payslip-${year}-${String(month).padStart(2, "0")}.pdf`;
      const uri = `${FileSystem.cacheDirectory}${filename}`;
      await FileSystem.writeAsStringAsync(uri, base64, { encoding: FileSystem.EncodingType.Base64 });
      if (await Sharing.isAvailableAsync()) {
        await Sharing.shareAsync(uri, { mimeType: "application/pdf", dialogTitle: filename });
        setNotice(`PDF payslip for ${month}/${year} is ready to save or share.`);
      } else {
        setNotice(`PDF saved to the app cache: ${filename}`);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to download payslip.");
    } finally {
      setLoading(false);
    }
  };

  const loadOnboarding = useCallback(async () => {
    try {
      setUnionAssignment(null);
      const [profile, assignment] = await Promise.all([
        apiFetch("/api/v1/driver/onboarding/me"),
        apiFetch("/api/v1/driver/union")
      ]);
      setUnionAssignment(assignment?.status === "Active" ? assignment : null);
      if (!profile?.driverId) setLegacyServiceClass(null);
      if (profile?.driverId) {
        setOnboardingProfile(profile);
        setProfilePhotoBase64(profile.profilePhotoBase64 ?? "");
        const storedServiceClass = String(profile.serviceClass ?? "").trim().toLowerCase();
        setLegacyServiceClass(storedServiceClass && storedServiceClass !== "cab" ? storedServiceClass : null);
        setDriverName(profile.fullName ?? "");
        setPhoneNumber(profile.phoneNumber ?? "");
        setVehicleMake(profile.vehicleMake ?? "");
        setVehicleModel(profile.vehicleModel ?? "");
        setVehicleRegistration(profile.vehicleRegistration ?? "");
        setLicenceReference(profile.documents?.find((document: any) => document.documentType === "DrivingLicence")?.storageReference ?? "");
        setRegistrationReference(profile.documents?.find((document: any) => document.documentType === "VehicleRegistration")?.storageReference ?? "");
      }
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to load onboarding profile."); }
  }, [apiFetch]);

  useEffect(() => { if (user) void loadOnboarding(); }, [user, loadOnboarding]);
  useEffect(() => {
    if (!user || tab !== "Profile") return;
    let cancelled = false;
    void apiFetch("/api/v1/driver/union")
      .then(assignment => {
        if (!cancelled) setUnionAssignment(assignment?.status === "Active" ? assignment : null);
      })
      .catch(error => setError(error instanceof Error ? error.message : "Unable to refresh Union membership."));
    return () => { cancelled = true; };
  }, [user, tab, apiFetch]);

  const captureSelfie = async () => {
    setError(""); setNotice("");
    try {
      const permission = await ImagePicker.requestCameraPermissionsAsync();
      if (!permission.granted) throw new Error("Camera permission is required to capture your driver selfie.");
      const result = await ImagePicker.launchCameraAsync({
        mediaTypes: ["images"],
        cameraType: ImagePicker.CameraType.front,
        allowsEditing: true,
        aspect: [1, 1],
        quality: 0.55,
        base64: true
      });
      if (result.canceled) return;
      const asset = result.assets[0];
      if (!asset?.uri) throw new Error("Unable to read the captured selfie. Please try again.");
      const compressed = await ImageManipulator.manipulateAsync(
        asset.uri,
        [{ resize: { width: 512, height: 512 } }],
        { compress: 0.55, format: ImageManipulator.SaveFormat.JPEG, base64: true }
      );
      const photo = compressed.base64;
      if (!photo) throw new Error("Unable to compress the captured selfie. Please try again.");
      if (photo.length > 410_000) throw new Error("Selfie is too large. Retake it in good lighting.");
      setProfilePhotoBase64(photo);
      if (onboardingProfile?.driverId) {
        const updated = await apiFetch("/api/v1/driver/onboarding/selfie", {
          method: "PUT",
          body: JSON.stringify({ profilePhotoBase64: photo })
        });
        setOnboardingProfile(updated);
        setNotice("Driver selfie saved to your profile.");
      } else {
        setNotice("Selfie captured. It will be saved when you submit onboarding.");
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "Unable to capture driver selfie.");
    }
  };

  const submitOnboarding = async () => {
    if (legacyServiceClass) {
      setError("This legacy non-Cab profile is retained for history and cannot be converted in place. Complete Cab vehicle verification through your union.");
      return;
    }
    setLoading(true); setError(""); setNotice("");
    try {
      const profile = await apiFetch("/api/v1/driver/onboarding", {
        method: "POST",
        body: JSON.stringify({
          zoneId: ZONE,
          fullName: driverName,
          phoneNumber,
          vehicleMake,
          vehicleModel,
          vehicleRegistration,
          serviceClass,
          passengerCapacity: 3,
          profilePhotoBase64,
          documents: [
            { documentType: "DrivingLicence", storageReference: licenceReference },
            { documentType: "VehicleRegistration", storageReference: registrationReference }
          ]
        })
      });
      setOnboardingProfile(profile);
      setNotice("Onboarding submitted. Queue and ride access will unlock after approval.");
    } catch (e) { setError(e instanceof Error ? e.message : "Unable to submit onboarding."); }
    finally { setLoading(false); }
  };

  const logout = async () => {
    try { if (tokens) await jsonRequest("/api/v1/auth/logout", { method: "POST" }, tokens.accessToken); }
    catch { /* Local credentials are cleared even if the network is unavailable. */ }
    await clearTokens(); setTokens(null); setUser(null); setQueue([]); setMembership({ isQueued: false, position: null }); setActiveRide(null); setDriverRideHistory([]); setAvailableRides([]); setUnionAssignment(null);
  };

  const resolvedTheme = themePreference === "system" ? (systemColorScheme ?? "light") : themePreference;
  const colors = resolvedTheme === "dark" ? darkColors : lightColors;
  const styles = createStyles(colors);
  const themePicker = <View style={styles.themePicker}>
    {(["system", "light", "dark"] as const).map(option => <Pressable key={option} onPress={() => selectTheme(option)} style={[styles.themeOption, themePreference === option && styles.themeOptionSelected]}><Text style={[styles.themeOptionText, themePreference === option && styles.themeOptionTextSelected]}>{option === "system" ? "System" : option === "light" ? "Light" : "Dark"}</Text></Pressable>)}
  </View>;
  if (loading && !user) return <SafeAreaView style={styles.safe}><ActivityIndicator color={colors.accent} style={{ marginTop: 80 }} /></SafeAreaView>;
  if (!user) return (
    <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"} />
      <View style={styles.header}><TurnOvaLogo subtitle="Everyone’s Turn Matters." /></View>
      <ScrollView contentContainerStyle={styles.content}>
        <Text style={styles.eyebrow}>DRIVER PARTNER</Text><Text style={styles.title}>{mode === "login" ? "Welcome back" : "Create your account"}</Text>
        <Text style={styles.sub}>Sign in securely with your email and password.</Text>{themePicker}
        <View style={styles.card}>
          {mode === "register" && <TextInput value={name} onChangeText={setName} style={styles.input} placeholderTextColor={colors.muted} placeholder="Full name" autoCapitalize="words" />}
          <TextInput value={email} onChangeText={setEmail} style={styles.input} placeholderTextColor={colors.muted} placeholder="Email address" keyboardType="email-address" autoCapitalize="none" autoCorrect={false} />
          <TextInput value={password} onChangeText={setPassword} style={styles.input} placeholderTextColor={colors.muted} placeholder="Password" secureTextEntry />
          <Pressable style={styles.primary} disabled={loading || !email.trim() || !password} onPress={() => void authenticate()}><Text style={styles.primaryText}>{loading ? "Please wait…" : mode === "login" ? "Sign in" : "Create driver account"}</Text></Pressable>
          <Pressable onPress={() => { setMode(mode === "login" ? "register" : "login"); setError(""); }}><Text style={styles.link}>{mode === "login" ? "New to TurnOva? Create an account" : "Already registered? Sign in"}</Text></Pressable>
          {!!error && <Text style={styles.error}>{error}</Text>}
        </View>
      </ScrollView>
    </SafeAreaView>
  );

  const position = membership.position;
  const tabs = ["Home", "Queue", "Trips", "Payslips", "Profile"] as const;
  return <SafeAreaView style={styles.safe}><StatusBar style={resolvedTheme === "dark" ? "light" : "dark"} />
    <View style={styles.header}><TurnOvaLogo subtitle={`Driver partner · ${user.name || user.email}`} compact /><Text style={styles.role}>DRIVER</Text></View>
    <ScrollView contentContainerStyle={styles.content}>
      {legacyServiceClass && <View style={styles.card}><Text style={styles.error}>Legacy {legacyServiceClass.toUpperCase()} profile retained for history. It is not eligible for new Cab rides. Complete Cab vehicle verification through your union before joining Cab discovery.</Text></View>}
      {tab === "Home" && <><Text style={styles.eyebrow}>YOUR DRIVER SPACE</Text><Text style={styles.title}>Welcome, {user.name || "Driver"}</Text><Text style={styles.sub}>Your next opportunity starts with a fair turn.</Text><View style={styles.card}><Text style={styles.label}>SERVICE ZONE</Text><Text style={styles.cardTitle}>Hyderabad Central · {legacyServiceClass ? `LEGACY ${legacyServiceClass.toUpperCase()} · INELIGIBLE` : serviceClass.toUpperCase()}</Text><Text style={styles.label}>AVAILABILITY</Text><Text style={styles.cardTitle}>{availabilityStatus}</Text><Text style={styles.label}>YOUR QUEUE POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "—"}</Text><Pressable style={styles.primary} onPress={() => setTab("Queue")}><Text style={styles.primaryText}>Manage availability</Text></Pressable></View></>}
      {tab === "Queue" && <><Text style={styles.eyebrow}>HYDERABAD CENTRAL · {legacyServiceClass ? `LEGACY ${legacyServiceClass.toUpperCase()} · INELIGIBLE` : serviceClass.toUpperCase()}</Text><Text style={styles.title}>Your queue</Text><View style={styles.card}><Text style={styles.label}>AVAILABILITY</Text><Text style={styles.position}>{availabilityStatus}</Text><Pressable style={styles.primary} onPress={() => void toggleAvailability()} disabled={loading || (!!legacyServiceClass && availabilityStatus !== "Online")}><Text style={styles.primaryText}>{loading ? "Please wait…" : availabilityStatus === "Online" ? "Go offline" : "Go online"}</Text></Pressable><Text style={styles.label}>CURRENT POSITION</Text><Text style={styles.position}>{position ? `#${position}` : "Not joined"}</Text><Pressable style={styles.primary} onPress={() => void loadQueue()} disabled={loading}><Text style={styles.primaryText}>{loading ? "Please wait…" : "Refresh queue"}</Text></Pressable><Pressable onPress={() => void loadQueue()}><Text style={styles.link}>Refresh list</Text></Pressable></View>{queue.map(item => <View style={styles.queueItem} key={item.driverId}><Text style={styles.cardTitle}>#{item.position} · {item.driverId}</Text><Text style={styles.muted}>{new Date(item.joinedAt).toLocaleString()}</Text></View>)}</>}
      {tab === "Trips" && <>
        <Text style={styles.title}>Trips</Text>
        <View style={styles.card}>
          <Text style={styles.label}>RIDE REQUESTS</Text>
          <Pressable style={styles.primary} onPress={() => void loadAvailableRides()}><Text style={styles.primaryText}>Refresh requests</Text></Pressable>
          {availableRides.map(item=><View key={item.rideId} style={{marginTop:14}}>
            <Text style={styles.cardTitle}>{item.pickup} → {item.dropoff}</Text>
            <Text style={styles.muted}>{item.passengers} passenger(s) · {item.rideId}</Text>
            <View style={{ flexDirection: "row", gap: 10 }}>
              <Pressable style={[styles.secondary, { flex: 1 }]} onPress={()=>void acceptRide(item.rideId)}><Text style={styles.secondaryText}>Accept ride</Text></Pressable>
              <Pressable style={[styles.secondary, { flex: 1 }]} onPress={()=>void rejectRide(item.rideId)}><Text style={styles.secondaryText}>Decline</Text></Pressable>
            </View>
          </View>)}
          {activeRide&&<>
            <Text style={styles.label}>{["Negotiating","AgreementConfirmed"].includes(activeRide.status)?"SELECTED RIDE":"ACTIVE TRIP"}</Text>
            <Text style={styles.success}>{activeRide.status} · {activeRide.rideId} · {activeRide.locationSharingActive?"Location sharing active":"Location sharing stopped"}</Text>
            <Text style={styles.cardTitle}>{activeRide.pickup} → {activeRide.dropoff}</Text>
            {activeRide.stops?.length ? <Text style={styles.muted}>Stops · {activeRide.stops.join(" → ")}</Text> : null}
            <View style={{marginTop:12,paddingTop:10,borderTopWidth:1,borderTopColor:colors.border}}>
              <Text style={styles.cardTitle}>Direct fare negotiation · INR</Text>
              {agreementHistory.map(item=><View key={item.version} style={{paddingVertical:7}}>
                <Text style={styles.muted}>Version {item.version} · ₹{item.amount.toFixed(2)} · proposed by {item.proposedBy}</Text>
                <Text style={styles.muted}>Rider: {item.riderConfirmed?"Confirmed":"Pending"} · Driver: {item.driverConfirmed?"Confirmed":"Pending"}</Text>
              </View>)}
              <Pressable style={styles.secondary} onPress={()=>void loadAgreementHistory(activeRide.rideId)}><Text style={styles.secondaryText}>Refresh agreement</Text></Pressable>
              {["Negotiating","AgreementConfirmed","Accepted","Arrived","InProgress"].includes(activeRide.status)&&<>
                <TextInput value={agreementAmount} onChangeText={setAgreementAmount} keyboardType="decimal-pad" style={styles.input} placeholderTextColor={colors.muted} placeholder="Propose / counter amount (₹)"/>
                <Pressable style={styles.secondary} disabled={!agreementAmount.trim()} onPress={()=>void proposeAgreement()}><Text style={styles.secondaryText}>Propose / counter fare</Text></Pressable>
                {agreementHistory.length>0&&<Pressable style={styles.secondary} disabled={agreementHistory[agreementHistory.length-1].driverConfirmed} onPress={()=>void confirmAgreement()}><Text style={styles.secondaryText}>{agreementHistory[agreementHistory.length-1].driverConfirmed?"You confirmed this version":"Confirm current agreement"}</Text></Pressable>}
              </>}
            </View>
            {activeRide.status==="Negotiating"&&<><Text style={styles.muted}>A Rider selected you. Propose or counter the fare directly, then both parties confirm the current version. {activeRide.negotiationExpiresAtUtc ? "Negotiation expires at "+new Date(activeRide.negotiationExpiresAtUtc).toLocaleTimeString() : "A configured negotiation deadline applies."}</Text><Pressable style={styles.secondary} onPress={()=>void rejectRide(activeRide.rideId)}><Text style={styles.secondaryText}>Decline selected request</Text></Pressable></>}
            {activeRide.status==="AgreementConfirmed"&&<><Text style={styles.muted}>Both parties confirmed the current fare. Accept this selected trip to begin the arrival/start flow.</Text><Pressable style={styles.primary} onPress={()=>void acceptRide(activeRide.rideId)}><Text style={styles.primaryText}>Accept agreed trip</Text></Pressable><Pressable style={styles.secondary} onPress={()=>void rejectRide(activeRide.rideId)}><Text style={styles.secondaryText}>Decline selected request</Text></Pressable></>}
            {activeRide.status==="Accepted"&&<Pressable style={styles.secondary} onPress={()=>void advanceRide("arrive")}><Text style={styles.secondaryText}>Mark arrived</Text></Pressable>}
            {activeRide.status==="Arrived"&&<><Text style={styles.muted}>{agreementHistory.length>0&&agreementHistory[agreementHistory.length-1].riderConfirmed&&agreementHistory[agreementHistory.length-1].driverConfirmed?"Both parties confirmed the current fare.":"Both Rider and Driver must confirm the current fare before starting."}</Text><Pressable style={styles.secondary} disabled={!agreementHistory.length||!agreementHistory[agreementHistory.length-1].riderConfirmed||!agreementHistory[agreementHistory.length-1].driverConfirmed} onPress={()=>void advanceRide("start")}><Text style={styles.secondaryText}>Start trip</Text></Pressable></>}
            {activeRide.status==="InProgress"&&<Pressable style={styles.secondary} disabled={!agreementHistory.length||!agreementHistory[agreementHistory.length-1].riderConfirmed||!agreementHistory[agreementHistory.length-1].driverConfirmed} onPress={()=>void advanceRide("complete")}><Text style={styles.secondaryText}>Complete trip</Text></Pressable>}
          </>}
        </View>
        {driverRideHistory.filter(item=>item.rideId!==activeRide?.rideId).length>0&&<View style={styles.card}>
          <Text style={styles.cardTitle}>Trip history</Text>
          {driverRideHistory.filter(item=>item.rideId!==activeRide?.rideId).map(item=><View key={item.rideId} style={{paddingVertical:12,borderBottomWidth:1,borderBottomColor:colors.border}}>
            <Text style={styles.label}>{item.status.toUpperCase()}</Text>
            <Text style={styles.cardTitle}>{item.pickup} → {item.dropoff}</Text>
            <Text style={styles.muted}>{item.serviceClass.toUpperCase()} · {item.passengers} passenger(s)</Text>
            <Text style={styles.muted}>{new Date(item.requestedAt).toLocaleString()}</Text>
            <Text style={styles.muted}>Reference · {item.rideId}</Text>
          </View>)}
        </View>}
        {!activeRide&&driverRideHistory.length===0&&<View style={styles.card}><Text style={styles.muted}>No trip history yet.</Text></View>}
      </>}
      {tab === "Payslips" && <><Text style={styles.eyebrow}>MONTHLY PDF DOCUMENTS</Text><Text style={styles.title}>Your payslips</Text><Text style={styles.sub}>Download a completed month's payslip as a PDF. Only finalized months are available.</Text><View style={styles.card}><Text style={styles.cardTitle}>Recent months</Text>{Array.from({ length: 12 }, (_, index) => { const date = new Date(); date.setDate(1); date.setMonth(date.getMonth() - index - 1); const year = date.getFullYear(); const month = date.getMonth() + 1; const label = date.toLocaleDateString(undefined, { month: "long", year: "numeric" }); return <View key={`${year}-${month}`} style={{ flexDirection: "row", alignItems: "center", justifyContent: "space-between", paddingVertical: 10, borderBottomWidth: 1, borderBottomColor: colors.border }}><Text style={styles.cardTitle}>{label}</Text><Pressable style={styles.secondary} disabled={loading} onPress={() => void downloadPayslip(year, month)}><Text style={styles.secondaryText}>Download PDF</Text></Pressable></View>; })}</View></>}
      {tab === "Profile" && <><Text style={styles.title}>Driver onboarding</Text>{themePicker}<Text style={styles.label}>APP THEME</Text><View style={styles.card}>
        <Text style={styles.cardTitle}>Verification status: {onboardingProfile?.status ?? "Not submitted"}</Text>
        {profilePhotoBase64 ? <Image source={{ uri: `data:image/jpeg;base64,${profilePhotoBase64}` }} style={{ width: 112, height: 112, borderRadius: 56, alignSelf: "center", marginVertical: 12 }} /> : <Text style={styles.muted}>No driver selfie saved yet.</Text>}
        {!!onboardingProfile?.profilePhotoApprovalStatus && <Text style={styles.muted}>Union selfie approval: {onboardingProfile.profilePhotoApprovalStatus}{onboardingProfile.profilePhotoApprovalStatus === "Rejected" && onboardingProfile.profilePhotoReviewReason ? ` · ${onboardingProfile.profilePhotoReviewReason}` : ""}</Text>}
        <View style={{ marginTop: 14, paddingTop: 12, borderTopWidth: 1, borderTopColor: colors.border }}>
          <Text style={styles.label}>CURRENT UNION MEMBERSHIP</Text>
          {unionAssignment ? <>
            <Text style={styles.cardTitle}>{unionAssignment.unionName}</Text>
            <Text style={styles.muted}>Membership status: {unionAssignment.status}</Text>
          </> : <Text style={styles.muted}>No active Cab union membership is assigned yet.</Text>}
        </View>
        <Pressable style={styles.secondary} onPress={() => void captureSelfie()} disabled={loading || !!legacyServiceClass}><Text style={styles.secondaryText}>{profilePhotoBase64 ? "Retake driver selfie" : "Take driver selfie"}</Text></Pressable>
        {!!onboardingProfile?.reviewReason && <Text style={styles.error}>Review note: {onboardingProfile.reviewReason}</Text>}
        {onboardingProfile?.status === "Approved" && legacyServiceClass ? <Text style={styles.error}>Your legacy {legacyServiceClass.toUpperCase()} profile is retained for history and is not eligible for new Cab rides.</Text> : onboardingProfile?.status === "Approved" ? <Text style={styles.success}>Your Cab driver profile is approved. Active union membership and verified vehicle documents are also required for Rider discovery.</Text> : onboardingProfile?.status === "Suspended" ? <Text style={styles.error}>Your driver account is suspended. Contact TurnOva operations for assistance.</Text> : <>
          <Text style={styles.sub}>Complete your driver and vehicle details and capture a clear front-facing selfie. Your application will remain pending until reviewed.</Text>
          <Text style={styles.label}>VEHICLE CATEGORY</Text>
          <View style={[styles.themeOption, styles.themeOptionSelected]}><Text style={styles.themeOptionTextSelected}>CAB · ONLY MVP CATEGORY</Text></View>
          <Text style={styles.muted}>New TurnOva onboarding accepts Cab vehicles only.</Text>
          <TextInput value={driverName} onChangeText={setDriverName} style={styles.input} placeholderTextColor={colors.muted} placeholder="Full legal name" />
          <TextInput value={phoneNumber} onChangeText={setPhoneNumber} style={styles.input} placeholderTextColor={colors.muted} placeholder="Phone number with country code" keyboardType="phone-pad" />
          <TextInput value={vehicleMake} onChangeText={setVehicleMake} style={styles.input} placeholderTextColor={colors.muted} placeholder="Vehicle make (e.g. Maruti)" />
          <TextInput value={vehicleModel} onChangeText={setVehicleModel} style={styles.input} placeholderTextColor={colors.muted} placeholder="Vehicle model" />
          <TextInput value={vehicleRegistration} onChangeText={setVehicleRegistration} style={styles.input} placeholderTextColor={colors.muted} placeholder="Vehicle registration number" autoCapitalize="characters" />
          <Text style={styles.label}>PRIVATE DOCUMENT STORAGE REFERENCES</Text>
          <Text style={styles.muted}>Upload files to your approved private storage workflow first. Enter opaque object references, not public URLs.</Text>
          <TextInput value={licenceReference} onChangeText={setLicenceReference} style={styles.input} placeholderTextColor={colors.muted} placeholder="Driving licence object reference" autoCapitalize="none" />
          <TextInput value={registrationReference} onChangeText={setRegistrationReference} style={styles.input} placeholderTextColor={colors.muted} placeholder="Vehicle RC object reference" autoCapitalize="none" />
          <Pressable style={styles.primary} disabled={loading || !driverName.trim() || !phoneNumber.trim() || !vehicleMake.trim() || !vehicleModel.trim() || !vehicleRegistration.trim() || !licenceReference.trim() || !registrationReference.trim() || !profilePhotoBase64} onPress={() => void submitOnboarding()}><Text style={styles.primaryText}>{loading ? "Please wait…" : onboardingProfile ? "Resubmit for review" : "Submit for approval"}</Text></Pressable>
        </>}
      </View><View style={styles.card}><Text style={styles.cardTitle}>{user.name || "Driver"}</Text><Text style={styles.muted}>{user.email}</Text><Text style={styles.muted}>Account role: {user.role}</Text><Pressable style={styles.secondary} onPress={() => void logout()}><Text style={styles.secondaryText}>Sign out</Text></Pressable></View></>}
      {!!error && <Text style={styles.error}>{error}</Text>}{!!notice && <Text style={styles.success}>{notice}</Text>}
    </ScrollView>
    <View style={styles.tabBar}>{tabs.map(item => <Pressable key={item} style={styles.tab} onPress={() => setTab(item)}><Text style={[styles.tabText, tab === item && styles.tabActive]}>{item}</Text></Pressable>)}</View>
  </SafeAreaView>;
}

function createStyles(colors: ThemeColors) {
  return StyleSheet.create({
  safe:{flex:1,backgroundColor:colors.background},header:{backgroundColor:colors.header,padding:22,paddingTop:18},brand:{color:"#FFFFFF",fontSize:25,fontWeight:"800"},tagline:{color:colors.subtle,marginTop:4,fontSize:12},role:{color:colors.tabActive,fontWeight:"800",marginTop:6},content:{padding:22,paddingBottom:34},eyebrow:{color:colors.tabActive,fontWeight:"800",fontSize:11,letterSpacing:1.3,marginTop:8},title:{color:colors.text,fontSize:28,fontWeight:"800",marginTop:8},sub:{color:colors.muted,fontSize:14,marginTop:6,marginBottom:20},card:{backgroundColor:colors.surface,padding:20,borderRadius:18,marginBottom:15},input:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:14,color:colors.text,marginBottom:12},primary:{backgroundColor:colors.accent,borderRadius:12,padding:15,alignItems:"center",marginTop:12},primaryText:{color:colors.accentText,fontWeight:"800"},link:{color:colors.tabActive,fontWeight:"800",textAlign:"center",marginTop:18},label:{color:colors.label,fontSize:10,fontWeight:"800",letterSpacing:1.1,marginTop:10},cardTitle:{color:colors.text,fontSize:18,fontWeight:"800",marginVertical:8},position:{color:colors.tabActive,fontSize:42,fontWeight:"900",marginTop:6},muted:{color:colors.muted,fontSize:13,marginTop:5},queueItem:{backgroundColor:colors.surface,padding:14,borderRadius:12,marginBottom:8},error:{color:colors.danger,marginTop:12},success:{color:colors.success,marginTop:12},secondary:{borderWidth:1,borderColor:colors.border,borderRadius:12,padding:13,alignItems:"center",marginTop:20},secondaryText:{color:colors.text,fontWeight:"800"},tabBar:{flexDirection:"row",backgroundColor:colors.surface,paddingVertical:14},tab:{flex:1,alignItems:"center"},tabText:{color:colors.tabInactive,fontSize:12,fontWeight:"700"},tabActive:{color:colors.tabActive},themePicker:{flexDirection:"row",gap:8,marginTop:10,marginBottom:10},themeOption:{flex:1,paddingVertical:10,borderRadius:10,borderWidth:1,borderColor:colors.border,alignItems:"center",backgroundColor:colors.surface},themeOptionSelected:{backgroundColor:colors.header,borderColor:colors.header},themeOptionText:{color:colors.muted,fontWeight:"700",fontSize:12},themeOptionTextSelected:{color:"#FFFFFF",fontWeight:"800"}
});
}
