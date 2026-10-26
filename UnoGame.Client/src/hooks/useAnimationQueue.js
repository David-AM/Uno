import { useEffect, useRef, useState } from "react";
import Swal from "sweetalert2";

export function useAnimationQueue(game) {
  const [animationQueue, setAnimationQueue] = useState([]);
  const [activePlay, setActivePlay] = useState(null);
  const [displayedTopCard, setDisplayedTopCard] = useState(null);
  const [displayedHand, setDisplayedHand] = useState([]);
  const lastEventsSignature = useRef(null);

  useEffect(() => {
    if (!game?.recentEvents?.length) {
      lastEventsSignature.current = null;
      return;
    }

    const signature = JSON.stringify(game.recentEvents);
    if (signature === lastEventsSignature.current) return;
    lastEventsSignature.current = signature;

    const events = game.recentEvents.map((event, index) => ({
      ...event,
      animationId: `${Date.now()}-${index}-${Math.random()}`,
    }));
    const forfeitureEvents = events.filter((event) => event.type === "forfeit");
    const regularEvents = events.filter((event) => event.type !== "forfeit");
    setAnimationQueue((queue) => [
      ...forfeitureEvents,
      ...queue,
      ...regularEvents,
    ]);
  }, [game?.recentEvents]);

  useEffect(() => {
    const hasForfeitAlert = game?.recentEvents?.some(
      (event) => event.type === "forfeit",
    );
    if (game?.status !== "playing" && !hasForfeitAlert) Swal.close();
  }, [game?.status, game?.recentEvents]);

  useEffect(() => {
    if (!game || game.recentEvents?.length) return;
    if (game.topCard) setDisplayedTopCard(game.topCard);
    if (game.hand) setDisplayedHand(game.hand);
  }, [game?.gameId, game?.recentEvents?.length, game?.topCard, game?.hand]);

  useEffect(() => {
    const currentEvent = animationQueue[0];
    if (!currentEvent) {
      setActivePlay(null);
      return undefined;
    }

    const showToast = (icon) => Swal.fire({
      toast: true,
      position: "top",
      icon,
      title: currentEvent.message,
      showConfirmButton: false,
      timer: 1400,
      timerProgressBar: true,
      customClass: { popup: "uno-swal-popup", title: "uno-swal-title" },
    });

    if (currentEvent.type === "table") {
      setDisplayedTopCard(currentEvent.card);
      if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
      setAnimationQueue((queue) => queue.slice(1));
      return undefined;
    }

    if (currentEvent.type === "draw") {
      if (game.status !== "playing") {
        if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
        setActivePlay(null);
        setAnimationQueue((queue) => queue.slice(1));
        return undefined;
      }

      setActivePlay(currentEvent);
      const timer = window.setTimeout(() => {
        setDisplayedHand(game.hand || []);
        setAnimationQueue((queue) => queue.slice(1));
      }, 800);
      return () => window.clearTimeout(timer);
    }

    if (currentEvent.type === "alert" || currentEvent.type === "forfeit") {
      setActivePlay(null);
      if (game.status !== "playing" && currentEvent.type !== "forfeit") {
        if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
        setAnimationQueue((queue) => queue.slice(1));
        return undefined;
      }

      let cancelled = false;
      showToast(currentEvent.type === "forfeit" ? "error" : "warning").then(() => {
        setDisplayedHand(game.hand || []);
        if (!cancelled) setAnimationQueue((queue) => queue.slice(1));
      });
      return () => {
        cancelled = true;
        Swal.close();
      };
    }

    setDisplayedTopCard(currentEvent.card);
    setActivePlay(currentEvent);
    if (game.status !== "playing") {
      setDisplayedHand(game.hand || []);
      setActivePlay(null);
      setAnimationQueue((queue) => queue.slice(1));
      return undefined;
    }

    let cancelled = false;
    showToast("warning").then(() => {
      setDisplayedHand(game.hand || []);
      if (!cancelled) setAnimationQueue((queue) => queue.slice(1));
    });
    return () => {
      cancelled = true;
      Swal.close();
    };
  }, [animationQueue[0]?.animationId, game?.status]);

  return {
    animationQueue,
    activePlay,
    displayedTopCard,
    displayedHand,
    setDisplayedHand,
  };
}
